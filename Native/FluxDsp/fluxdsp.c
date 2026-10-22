/* SPDX-License-Identifier: GPL-3.0-only
 * In-place 20/31-band peaking EQ for the pinned VLC 3 plugin SDK.
 * Control updates use a shared-memory sequence lock; the audio path never waits
 * for the UI, allocates memory, performs file I/O or calls managed code.
 */
#define MODULE_STRING "fluxdsp"
#define __PLUGIN__
#define DLL_EXPORT
#define _CRT_NONSTDC_NO_DEPRECATE
#include <stdint.h>
#include <winsock2.h>
#include <windows.h>
#include <math.h>
#include <stddef.h>
typedef intptr_t ssize_t;
#define poll WSAPoll
#include <vlc_common.h>
#include <vlc_plugin.h>
#include <vlc_aout.h>
#include <vlc_filter.h>
#include <vlc_block.h>
#include <vlc/libvlc.h>

#define DSP_MAGIC 0x46584450u
#define DSP_PROTOCOL 1u
#define DSP_BANDS 31
#define DSP_CHANNELS 32
#define DSP_FADE_FRAMES 256
#define DSP_MAP_BYTES 1024
#define DSP_PI 3.14159265358979323846

typedef struct {
    uint32_t magic, protocol;
    volatile LONG sequence;
    int32_t band_count, enabled;
    float preamp;
    int32_t anti_clip;
    float bandwidth;
    float gains[DSP_BANDS];
    float frequencies[DSP_BANDS];
} dsp_config;

typedef struct {
    volatile LONG active_count;
    uint32_t sample_rate, channels;
    volatile LONG applied_sequence;
    float effective_preamp;
    volatile LONG error;
} dsp_status;

typedef struct {
    dsp_config config;
    uint8_t padding[512 - sizeof(dsp_config)];
    dsp_status status;
} dsp_shared;

_Static_assert(sizeof(dsp_config) == 280, "Control protocol size");
_Static_assert(offsetof(dsp_config, gains) == 32, "Gain protocol offset");
_Static_assert(offsetof(dsp_config, frequencies) == 156, "Frequency protocol offset");
_Static_assert(offsetof(dsp_shared, status) == 512, "Status protocol offset");

typedef struct { double b0, b1, b2, a1, a2; } biquad;
typedef struct { double z1, z2; } history;
struct filter_sys_t {
    HANDLE mapping;
    dsp_shared *shared;
    dsp_config config;
    unsigned channels, rate, section_count, old_count, fade_left;
    biquad sections[DSP_BANDS], old_sections[DSP_BANDS];
    history states[DSP_BANDS][DSP_CHANNELS], old_states[DSP_BANDS][DSP_CHANNELS];
    double gain, target_gain;
};

static bool ReadConfig(dsp_shared *shared, dsp_config *result)
{
    LONG before = shared->config.sequence;
    if (before & 1) return false;
    MemoryBarrier();
    memcpy(result, &shared->config, sizeof(*result));
    MemoryBarrier();
    if (before != shared->config.sequence || result->magic != DSP_MAGIC || result->protocol != DSP_PROTOCOL)
        return false;
    if (result->band_count != 20 && result->band_count != 31) return false;
    if (!isfinite(result->preamp) || result->preamp < -20.f || result->preamp > 20.f
        || !isfinite(result->bandwidth) || result->bandwidth < 0.1f || result->bandwidth > 2.f) return false;
    float previous = 0.f;
    for (int band = 0; band < result->band_count; band++) {
        float gain = result->gains[band], frequency = result->frequencies[band];
        if (!isfinite(gain) || gain < -20.f || gain > 20.f || !isfinite(frequency)
            || frequency <= previous || frequency < 10.f || frequency > 20000.f) return false;
        previous = frequency;
    }
    return true;
}

static double PowerAt(const biquad *sections, unsigned count, double frequency, unsigned rate)
{
    double angle = 2.0 * DSP_PI * frequency / rate;
    double c1 = cos(angle), s1 = sin(angle), c2 = 2.0 * c1 * c1 - 1.0, s2 = 2.0 * s1 * c1, result = 1.0;
    for (unsigned index = 0; index < count; index++) {
        const biquad *s = &sections[index];
        double nr = s->b0 + s->b1 * c1 + s->b2 * c2, ni = s->b1 * s1 + s->b2 * s2;
        double dr = 1.0 + s->a1 * c1 + s->a2 * c2, di = s->a1 * s1 + s->a2 * s2;
        double denominator = dr * dr + di * di;
        if (denominator <= 1e-300) return 1e60;
        result *= (nr * nr + ni * ni) / denominator;
    }
    return result;
}

static void Configure(filter_sys_t *sys, const dsp_config *config, bool initial)
{
    if (!initial) {
        sys->old_count = sys->section_count;
        memcpy(sys->old_sections, sys->sections, sizeof(sys->sections));
        memcpy(sys->old_states, sys->states, sizeof(sys->states));
        sys->fade_left = DSP_FADE_FRAMES;
    }
    memset(sys->states, 0, sizeof(sys->states));
    sys->section_count = 0;
    double octave_ratio = pow(2.0, config->bandwidth);
    double quality = sqrt(octave_ratio) / (octave_ratio - 1.0);
    for (int band = 0; band < config->band_count; band++) {
        double frequency = config->frequencies[band], gain = config->gains[band];
        if (!config->enabled || fabs(gain) < 0.0001 || frequency >= sys->rate * 0.499) continue;
        double amplitude = pow(10.0, gain / 40.0);
        double angle = 2.0 * DSP_PI * frequency / sys->rate;
        double alpha = sin(angle) / (2.0 * quality), cosine = cos(angle);
        double a0 = 1.0 + alpha / amplitude;
        biquad *section = &sys->sections[sys->section_count++];
        section->b0 = (1.0 + alpha * amplitude) / a0;
        section->b1 = (-2.0 * cosine) / a0;
        section->b2 = (1.0 - alpha * amplitude) / a0;
        section->a1 = (-2.0 * cosine) / a0;
        section->a2 = (1.0 - alpha / amplitude) / a0;
    }
    double preamp = config->enabled ? config->preamp : 0.0;
    if (config->anti_clip && config->enabled) {
        double peak = 1.0;
        if (sys->section_count > 0) {
            double upper = fmin(22000.0, sys->rate * 0.499);
            for (int point = 0; point < 512; point++) {
                double frequency = 10.0 * pow(upper / 10.0, point / 511.0);
                peak = fmax(peak, PowerAt(sys->sections, sys->section_count, frequency, sys->rate));
            }
            for (int band = 0; band < config->band_count; band++)
                if (config->frequencies[band] < sys->rate * 0.499)
                    peak = fmax(peak, PowerAt(sys->sections, sys->section_count, config->frequencies[band], sys->rate));
        }
        double peak_db = 10.0 * log10(peak);
        if (peak_db > 0.01) peak_db += 0.25; /* margin for the sampled response envelope */
        preamp = fmin(preamp, -peak_db);
    }
    sys->target_gain = pow(10.0, preamp / 20.0);
    if (initial) sys->gain = sys->target_gain;
    memcpy(&sys->config, config, sizeof(*config));
    sys->shared->status.effective_preamp = (float)preamp;
    InterlockedExchange(&sys->shared->status.error, 0);
    InterlockedExchange(&sys->shared->status.applied_sequence, config->sequence);
}

static double Process(double value, unsigned channel, const biquad *sections,
    history states[DSP_BANDS][DSP_CHANNELS], unsigned count)
{
    for (unsigned index = 0; index < count; index++) {
        const biquad *s = &sections[index]; history *h = &states[index][channel];
        double output = s->b0 * value + h->z1;
        h->z1 = s->b1 * value - s->a1 * output + h->z2;
        h->z2 = s->b2 * value - s->a2 * output;
        value = output;
    }
    return value;
}

static block_t *Filter(filter_t *filter, block_t *block)
{
    filter_sys_t *sys = filter->p_sys;
    LONG sequence = sys->shared->config.sequence;
    if (sequence != sys->config.sequence && !(sequence & 1)) {
        dsp_config next;
        if (ReadConfig(sys->shared, &next)) Configure(sys, &next, false);
        else InterlockedExchange(&sys->shared->status.error, 1);
    }
    if (block->i_buffer / sizeof(float) / sys->channels < block->i_nb_samples) {
        InterlockedExchange(&sys->shared->status.error, 2); return block;
    }
    if (!sys->fade_left && sys->section_count == 0 && fabs(sys->gain - 1.0) < 1e-12 && sys->target_gain == 1.0)
        return block;
    float *samples = (float *)block->p_buffer;
    double gain_step = block->i_nb_samples ? (sys->target_gain - sys->gain) / block->i_nb_samples : 0.0;
    for (unsigned frame = 0; frame < block->i_nb_samples; frame++) {
        double blend = sys->fade_left ? (double)(DSP_FADE_FRAMES - sys->fade_left) / DSP_FADE_FRAMES : 1.0;
        sys->gain += gain_step;
        for (unsigned channel = 0; channel < sys->channels; channel++) {
            double input = *samples; if (!isfinite(input)) input = 0.0;
            double output = Process(input, channel, sys->sections, sys->states, sys->section_count);
            if (sys->fade_left) output = Process(input, channel, sys->old_sections, sys->old_states, sys->old_count) * (1.0 - blend) + output * blend;
            output *= sys->gain;
            float converted = (float)output;
            *samples++ = isfinite(converted) ? converted : 0.f;
        }
        if (sys->fade_left) sys->fade_left--;
    }
    sys->gain = sys->target_gain;
    for (unsigned band = 0; band < sys->section_count; band++)
        for (unsigned channel = 0; channel < sys->channels; channel++) {
            history *h = &sys->states[band][channel];
            if (fabs(h->z1) < 1e-25) h->z1 = 0.0;
            if (fabs(h->z2) < 1e-25) h->z2 = 0.0;
        }
    return block;
}

static void Flush(filter_t *filter)
{
    filter_sys_t *sys = filter->p_sys;
    memset(sys->states, 0, sizeof(sys->states)); memset(sys->old_states, 0, sizeof(sys->old_states));
    sys->fade_left = 0; sys->gain = sys->target_gain;
}

static int Open(vlc_object_t *object)
{
    filter_t *filter = (filter_t *)object;
    char *name = var_InheritString(object, "fluxdsp-map");
    if (!name) return VLC_EGENERIC;
    HANDLE mapping = OpenFileMappingA(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, name);
    libvlc_free(name); /* allocated by VLC's CRT, not this module's static CRT */
    if (!mapping) return VLC_EGENERIC;
    dsp_shared *shared = MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, DSP_MAP_BYTES);
    if (!shared) { CloseHandle(mapping); return VLC_EGENERIC; }
    dsp_config config;
    unsigned channels = aout_FormatNbChannels(&filter->fmt_in.audio), rate = filter->fmt_in.audio.i_rate;
    bool readable = ReadConfig(shared, &config);
    if (!readable || !config.enabled || !channels || channels > DSP_CHANNELS || rate < 8000 || rate > 768000) {
        if (readable && config.enabled) InterlockedExchange(&shared->status.error, 3);
        UnmapViewOfFile(shared); CloseHandle(mapping); return VLC_EGENERIC;
    }
    filter_sys_t *sys = calloc(1, sizeof(*sys));
    if (!sys) { UnmapViewOfFile(shared); CloseHandle(mapping); return VLC_ENOMEM; }
    sys->mapping = mapping; sys->shared = shared; sys->channels = channels; sys->rate = rate;
    Configure(sys, &config, true);
    filter->p_sys = sys;
    filter->fmt_in.audio.i_format = VLC_CODEC_FL32; aout_FormatPrepare(&filter->fmt_in.audio);
    filter->fmt_out.audio = filter->fmt_in.audio;
    filter->pf_audio_filter = Filter; filter->pf_flush = Flush;
    shared->status.sample_rate = rate; shared->status.channels = channels;
    InterlockedIncrement(&shared->status.active_count);
    msg_Dbg(filter, "FluxPlayer DSP active: %d independent bands, %u Hz, %u channels", config.band_count, rate, channels);
    return VLC_SUCCESS;
}

static void Close(vlc_object_t *object)
{
    filter_sys_t *sys = ((filter_t *)object)->p_sys;
    InterlockedDecrement(&sys->shared->status.active_count);
    UnmapViewOfFile(sys->shared); CloseHandle(sys->mapping); free(sys);
}

vlc_module_begin()
    set_shortname("FluxPlayer DSP")
    set_description("FluxPlayer native 20/31-band equalizer")
    set_capability("audio filter", 1000)
    add_shortcut("equalizer")
    add_string("fluxdsp-map", NULL, "Control map", "FluxPlayer shared control map", true)
        change_private()
    set_callbacks(Open, Close)
vlc_module_end()
