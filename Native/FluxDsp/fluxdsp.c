/* SPDX-License-Identifier: GPL-3.0-only
 * In-place 15/20/31-band peaking EQ for the pinned VLC 3 plugin SDK.
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
#define DSP_PROTOCOL 3u
#define DSP_REAL_EFFECTS 0x1BFFFu
#define DSP_BANDS 31
#define DSP_SECTIONS 48
#define DSP_CHANNELS 32
#define DSP_FADE_FRAMES 256
#define DSP_MAP_BYTES 4096
#define DSP_PI 3.14159265358979323846

typedef struct {
    uint32_t flags;
    float values[24], channel_gains[8], channel_delays[8], parametric[10][4];
    float matrix[64], level_target_db, level_max_gain_db, level_response_ms;
} effects_config;
typedef struct {
    uint32_t magic, protocol;
    volatile LONG sequence;
    int32_t band_count, enabled;
    float preamp;
    int32_t anti_clip;
    float bandwidth;
    float gains[DSP_BANDS];
    float frequencies[DSP_BANDS];
    effects_config effects;
} dsp_config;

typedef struct {
    volatile LONG active_count;
    uint32_t sample_rate, channels;
    volatile LONG applied_sequence;
    float effective_preamp;
    volatile LONG error;
    uint32_t effects;
    float input_peak, output_peak, input_rms, output_rms;
} dsp_status;

typedef struct {
    dsp_config config;
    uint8_t padding[1536 - sizeof(dsp_config)];
    dsp_status status;
} dsp_shared;

_Static_assert(sizeof(dsp_config) == 872, "Control protocol size");
_Static_assert(offsetof(dsp_config, gains) == 32, "Gain protocol offset");
_Static_assert(offsetof(dsp_config, frequencies) == 156, "Frequency protocol offset");
_Static_assert(offsetof(dsp_config, effects) == 280, "Effects protocol offset");
_Static_assert(offsetof(dsp_shared, status) == 1536, "Status protocol offset");

typedef struct { double b0, b1, b2, a1, a2; } biquad;
typedef struct { double z1, z2; } history;
struct filter_sys_t {
    HANDLE mapping;
    dsp_shared *shared;
    dsp_config config;
    unsigned channels, rate, section_count, old_count, fade_left;
    biquad sections[DSP_SECTIONS], old_sections[DSP_SECTIONS];
    history states[DSP_SECTIONS][DSP_CHANNELS], old_states[DSP_SECTIONS][DSP_CHANNELS];
    double gain, target_gain;
    float *delay_ring, *channel_ring;
    unsigned delay_frames, channel_frames, delay_cursor, channel_cursor;
    double crossfeed_state[2], compressor_env, flanger_phase, chorus_phase;
    double channel_gain[8], channel_delay[8], threshold, makeup, ceiling, attack, release, crossfeed_alpha;
    double level_energy, level_gain, level_alpha, level_target, level_max_gain, level_attack;
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
    if (result->band_count != 10 && result->band_count != 15 && result->band_count != 20 && result->band_count != 31) return false;
    if (!isfinite(result->preamp) || result->preamp < -20.f || result->preamp > 20.f
        || !isfinite(result->bandwidth) || result->bandwidth < 0.1f || result->bandwidth > 2.f) return false;
    float previous = 0.f;
    for (int band = 0; band < result->band_count; band++) {
        float gain = result->gains[band], frequency = result->frequencies[band];
        if (!isfinite(gain) || gain < -20.f || gain > 20.f || !isfinite(frequency)
            || frequency <= previous || frequency < 10.f || frequency > 20000.f) return false;
        previous = frequency;
    }
    const effects_config *fx = &result->effects;
    if (fx->flags & ~131071u) return false;
    for (unsigned row = 0; row < 8; row++) { double sum = 0; for (unsigned column = 0; column < 8; column++) { float coefficient = fx->matrix[row * 8 + column]; if (!isfinite(coefficient) || coefficient < -2 || coefficient > 2) return false; sum += fabs(coefficient); } if (sum > 4.001) return false; }
    if ((fx->flags & 65536u) && (!isfinite(fx->level_target_db) || fx->level_target_db < -30 || fx->level_target_db > -6 || !isfinite(fx->level_max_gain_db) || fx->level_max_gain_db < 0 || fx->level_max_gain_db > 18 || !isfinite(fx->level_response_ms) || fx->level_response_ms < 100 || fx->level_response_ms > 5000)) return false;
    static const float lower[24] = { -12,30,0,-60,1,.1f,10,0,-18,-1,0,10,0,0,0,0,0,.1f,1,0,.1f,2,300,0 };
    static const float upper[24] = { 12,500,1,0,20,200,2000,18,0,1,2,1500,.8f,1,1,.75f,1,5,8,1,5,20,2000,0 };
    for (unsigned index = 0; index < 24; index++) if (!isfinite(fx->values[index]) || (fx->flags && (fx->values[index] < lower[index] || fx->values[index] > upper[index]))) return false;
    for (unsigned index = 0; index < 8; index++) if (!isfinite(fx->channel_gains[index]) || fx->channel_gains[index] < -24 || fx->channel_gains[index] > 12 || !isfinite(fx->channel_delays[index]) || fx->channel_delays[index] < 0 || fx->channel_delays[index] > 200) return false;
    for (unsigned index = 0; index < 10; index++) {
        const float *p = fx->parametric[index];
        if (fx->flags && (!isfinite(p[0]) || p[0] < 20 || p[0] > 20000 || !isfinite(p[1]) || p[1] < -18 || p[1] > 18 || !isfinite(p[2]) || p[2] < .1f || p[2] > 12 || !isfinite(p[3]))) return false;
    }
    return true;
}

static void AddPeak(filter_sys_t *sys, double frequency, double gain, double quality)
{
    if (fabs(gain) < .0001 || frequency >= sys->rate * .499 || sys->section_count >= DSP_SECTIONS) return;
    double amplitude = pow(10.0, gain / 40.0), angle = 2.0 * DSP_PI * frequency / sys->rate;
    double alpha = sin(angle) / (2.0 * quality), cosine = cos(angle), a0 = 1.0 + alpha / amplitude;
    biquad *section = &sys->sections[sys->section_count++];
    section->b0 = (1.0 + alpha * amplitude) / a0; section->b1 = (-2.0 * cosine) / a0; section->b2 = (1.0 - alpha * amplitude) / a0;
    section->a1 = (-2.0 * cosine) / a0; section->a2 = (1.0 - alpha / amplitude) / a0;
}

static void AddBass(filter_sys_t *sys, double frequency, double gain)
{
    if (fabs(gain) < .0001 || frequency >= sys->rate * .499) return;
    double a = pow(10.0, gain / 40.0), angle = 2.0 * DSP_PI * frequency / sys->rate, c = cos(angle);
    double term = 2.0 * sqrt(a) * sin(angle) / sqrt(2.0), a0 = a + 1.0 + (a - 1.0) * c + term;
    biquad *s = &sys->sections[sys->section_count++];
    s->b0 = a * (a + 1.0 - (a - 1.0) * c + term) / a0;
    s->b1 = 2.0 * a * (a - 1.0 - (a + 1.0) * c) / a0;
    s->b2 = a * (a + 1.0 - (a - 1.0) * c - term) / a0;
    s->a1 = -2.0 * (a - 1.0 + (a + 1.0) * c) / a0; s->a2 = (a + 1.0 + (a - 1.0) * c - term) / a0;
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
        AddPeak(sys, frequency, gain, quality);
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
    const effects_config *fx = &config->effects; const float *v = fx->values;
    if (fx->flags & 1u) for (unsigned band = 0; band < 10; band++) if (fx->parametric[band][3] > .5f) AddPeak(sys, fx->parametric[band][0], fx->parametric[band][1], fx->parametric[band][2]);
    if (fx->flags & 2u) AddBass(sys, v[1], v[0]);
    if (fx->flags) {
        sys->threshold = pow(10.0, v[3] / 20.0); sys->makeup = pow(10.0, v[7] / 20.0); sys->ceiling = pow(10.0, v[8] / 20.0);
        sys->attack = exp(-1.0 / (.001 * v[5] * sys->rate)); sys->release = exp(-1.0 / (.001 * v[6] * sys->rate));
        sys->crossfeed_alpha = 1.0 - exp(-2.0 * DSP_PI * v[22] / sys->rate);
        for (unsigned channel = 0; channel < 8; channel++) { sys->channel_gain[channel] = pow(10.0, fx->channel_gains[channel] / 20.0); sys->channel_delay[channel] = .001 * fx->channel_delays[channel] * sys->rate; }
    }
    if (initial) sys->gain = sys->target_gain;
    if (fx->flags & 65536u) {
        sys->level_alpha = exp(-1.0 / (.001 * fx->level_response_ms * sys->rate)); sys->level_target = pow(10.0, fx->level_target_db / 20.0); sys->level_max_gain = pow(10.0, fx->level_max_gain_db / 20.0); sys->level_attack = exp(-1.0 / (.01 * sys->rate));
        if (initial || !(sys->config.effects.flags & 65536u)) { sys->level_energy = 0; sys->level_gain = 1; }
    }
    memcpy(&sys->config, config, sizeof(*config));
    sys->shared->status.effective_preamp = (float)preamp;
    InterlockedExchange(&sys->shared->status.error, 0);
    InterlockedExchange(&sys->shared->status.applied_sequence, config->sequence);
    sys->shared->status.effects = fx->flags;
}

static double Process(double value, unsigned channel, const biquad *sections,
    history states[DSP_SECTIONS][DSP_CHANNELS], unsigned count)
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

static double Tap(const float *ring, unsigned length, unsigned channels, unsigned cursor, unsigned channel, double delay)
{
    if (!ring || length < 2) return 0;
    delay = fmax(1.0, fmin(delay, length - 2.0));
    double position = cursor + length - delay; while (position >= length) position -= length;
    unsigned first = (unsigned)position, second = (first + 1) % length; double fraction = position - first;
    return ring[first * channels + channel] * (1.0 - fraction) + ring[second * channels + channel] * fraction;
}

static void Effects(filter_sys_t *sys, double *samples)
{
    const effects_config *fx = &sys->config.effects; const float *v = fx->values; uint32_t flags = fx->flags;
    if (!(flags & DSP_REAL_EFFECTS)) return;
    for (unsigned channel = 0; channel < sys->channels; channel++) {
        if ((flags & 64u) && channel < 8) samples[channel] *= sys->channel_gain[channel];
        if (sys->channel_ring) {
            sys->channel_ring[sys->channel_cursor * sys->channels + channel] = (float)samples[channel];
            if ((flags & 128u) && channel < 8 && sys->channel_delay[channel] >= 1) samples[channel] = Tap(sys->channel_ring, sys->channel_frames, sys->channels, sys->channel_cursor, channel, sys->channel_delay[channel]);
        }
    }
    if (flags & 32768u) { double input[8]; for (unsigned channel = 0; channel < sys->channels; channel++) input[channel] = samples[channel]; for (unsigned output = 0; output < sys->channels; output++) { double value = 0; for (unsigned channel = 0; channel < sys->channels; channel++) value += fx->matrix[output * 8 + channel] * input[channel]; samples[output] = value; } }
    if (sys->channels >= 2) {
        double l = samples[0], r = samples[1];
        if (flags & 4u) {
            sys->crossfeed_state[0] += sys->crossfeed_alpha * (l - sys->crossfeed_state[0]);
            sys->crossfeed_state[1] += sys->crossfeed_alpha * (r - sys->crossfeed_state[1]);
            samples[0] = (l + v[2] * sys->crossfeed_state[1]) / (1.0 + v[2]); samples[1] = (r + v[2] * sys->crossfeed_state[0]) / (1.0 + v[2]);
        }
        if (flags & 8192u) { double side = (samples[0] - samples[1]) * .5; samples[0] = side; samples[1] = -side; }
        if (flags & 256u) { double mid = (samples[0] + samples[1]) * .5, side = (samples[0] - samples[1]) * .5 * v[10]; samples[0] = mid + side; samples[1] = mid - side; }
        if (flags & 32u) { samples[0] *= 1.0 - fmax(0, v[9]); samples[1] *= 1.0 + fmin(0, v[9]); }
    }
    if (sys->delay_ring) {
        double flanger_delay = (flags & 2048u) ? (.001 * (1.0 + v[18] * (.5 + .5 * sin(sys->flanger_phase)))) * sys->rate : 0;
        double chorus_delay = (flags & 4096u) ? (.001 * (20.0 + v[21] * (.5 + .5 * sin(sys->chorus_phase)))) * sys->rate : 0;
        for (unsigned channel = 0; channel < sys->channels; channel++) {
            double dry = samples[channel], output = dry, feedback = 0;
            if (flags & 512u) { double tap = Tap(sys->delay_ring, sys->delay_frames, sys->channels, sys->delay_cursor, channel, .001 * v[11] * sys->rate); output += v[13] * tap; feedback += .6 * v[12] * tap; }
            if (flags & 1024u) { double reverb = (Tap(sys->delay_ring, sys->delay_frames, sys->channels, sys->delay_cursor, channel, .0297 * sys->rate) + Tap(sys->delay_ring, sys->delay_frames, sys->channels, sys->delay_cursor, channel, .0371 * sys->rate) + Tap(sys->delay_ring, sys->delay_frames, sys->channels, sys->delay_cursor, channel, .0411 * sys->rate)) / 3.0; output += v[14] * reverb; feedback += .5 * v[15] * reverb; }
            if (flags & 2048u) output += v[16] * Tap(sys->delay_ring, sys->delay_frames, sys->channels, sys->delay_cursor, channel, flanger_delay);
            if (flags & 4096u) output += v[19] * Tap(sys->delay_ring, sys->delay_frames, sys->channels, sys->delay_cursor, channel, chorus_delay + channel * .001 * sys->rate);
            sys->delay_ring[sys->delay_cursor * sys->channels + channel] = (float)(dry + feedback); samples[channel] = output;
        }
        sys->flanger_phase += 2.0 * DSP_PI * v[17] / sys->rate; sys->chorus_phase += 2.0 * DSP_PI * v[20] / sys->rate;
        if (sys->flanger_phase > 2.0 * DSP_PI) sys->flanger_phase -= 2.0 * DSP_PI; if (sys->chorus_phase > 2.0 * DSP_PI) sys->chorus_phase -= 2.0 * DSP_PI;
        sys->delay_cursor = (sys->delay_cursor + 1) % sys->delay_frames;
    }
    if (sys->channel_ring) sys->channel_cursor = (sys->channel_cursor + 1) % sys->channel_frames;
    if (flags & 65536u) {
        double energy = 0, input_peak = 0; for (unsigned channel = 0; channel < sys->channels; channel++) { energy += samples[channel] * samples[channel]; input_peak = fmax(input_peak, fabs(samples[channel])); }
        sys->level_energy = sys->level_alpha * sys->level_energy + (1.0 - sys->level_alpha) * energy / sys->channels;
        double rms = sqrt(fmax(0, sys->level_energy)); double desired = rms > .001 ? fmax(.0316227766, fmin(sys->level_max_gain, sys->level_target / rms)) : 1.0;
        double coefficient = desired < sys->level_gain ? sys->level_attack : sys->level_alpha; sys->level_gain = coefficient * sys->level_gain + (1.0 - coefficient) * desired;
        double applied = input_peak > 0 ? fmin(sys->level_gain, .98 / input_peak) : sys->level_gain;
        for (unsigned channel = 0; channel < sys->channels; channel++) samples[channel] *= applied;
    }
    double peak = 0; for (unsigned channel = 0; channel < sys->channels; channel++) peak = fmax(peak, fabs(samples[channel]));
    if (flags & 8u) {
        double coefficient = peak > sys->compressor_env ? sys->attack : sys->release;
        sys->compressor_env = coefficient * sys->compressor_env + (1.0 - coefficient) * peak;
        double reduction = sys->compressor_env > sys->threshold ? pow(sys->compressor_env / sys->threshold, 1.0 / v[4] - 1.0) : 1.0;
        for (unsigned channel = 0; channel < sys->channels; channel++) samples[channel] *= reduction * sys->makeup;
        peak *= reduction * sys->makeup;
    }
    if ((flags & 16u) && peak > sys->ceiling) for (unsigned channel = 0; channel < sys->channels; channel++) samples[channel] *= sys->ceiling / peak;
}

static block_t *Filter(filter_t *filter, block_t *block)
{
    filter_sys_t *sys = filter->p_sys;
    LONG sequence = sys->shared->config.sequence;
    if (sequence != sys->config.sequence && !(sequence & 1)) {
        dsp_config next;
        if (ReadConfig(sys->shared, &next) && (!(next.effects.flags & DSP_REAL_EFFECTS) || sys->delay_ring)) Configure(sys, &next, false);
        else InterlockedExchange(&sys->shared->status.error, 1);
    }
    if (block->i_buffer / sizeof(float) / sys->channels < block->i_nb_samples) {
        InterlockedExchange(&sys->shared->status.error, 2); return block;
    }
    if (!sys->config.effects.flags && !sys->fade_left && sys->section_count == 0 && fabs(sys->gain - 1.0) < 1e-12 && sys->target_gain == 1.0)
        return block;
    float *samples = (float *)block->p_buffer;
    bool effects_active = sys->config.effects.flags != 0;
    bool metering = (sys->config.effects.flags & 16384u) != 0;
    double input_peak = 0, output_peak = 0, input_sum = 0, output_sum = 0;
    double gain_step = block->i_nb_samples ? (sys->target_gain - sys->gain) / block->i_nb_samples : 0.0;
    for (unsigned frame = 0; frame < block->i_nb_samples; frame++) {
        double frame_samples[DSP_CHANNELS];
        double blend = sys->fade_left ? (double)(DSP_FADE_FRAMES - sys->fade_left) / DSP_FADE_FRAMES : 1.0;
        sys->gain += gain_step;
        for (unsigned channel = 0; channel < sys->channels; channel++) {
            double input = samples[channel]; if (!isfinite(input)) input = 0.0;
            if (metering) { input_peak = fmax(input_peak, fabs(input)); input_sum += input * input; }
            double output = Process(input, channel, sys->sections, sys->states, sys->section_count);
            if (sys->fade_left) output = Process(input, channel, sys->old_sections, sys->old_states, sys->old_count) * (1.0 - blend) + output * blend;
            output *= sys->gain;
            if (effects_active) frame_samples[channel] = output;
            else { float converted = (float)output; samples[channel] = isfinite(converted) ? converted : 0.f; }
        }
        if (effects_active) {
            Effects(sys, frame_samples);
            for (unsigned channel = 0; channel < sys->channels; channel++) { float converted = (float)frame_samples[channel]; samples[channel] = isfinite(converted) ? converted : 0.f; if (metering) { output_peak = fmax(output_peak, fabs(samples[channel])); output_sum += samples[channel] * (double)samples[channel]; } }
        }
        samples += sys->channels;
        if (sys->fade_left) sys->fade_left--;
    }
    sys->gain = sys->target_gain;
    if (metering && block->i_nb_samples) { double count = (double)block->i_nb_samples * sys->channels; sys->shared->status.input_peak = (float)input_peak; sys->shared->status.output_peak = (float)output_peak; sys->shared->status.input_rms = (float)sqrt(input_sum / count); sys->shared->status.output_rms = (float)sqrt(output_sum / count); }
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
    sys->compressor_env = 0; memset(sys->crossfeed_state, 0, sizeof(sys->crossfeed_state));
    sys->level_energy = 0; sys->level_gain = 1;
    if (sys->delay_ring) memset(sys->delay_ring, 0, (size_t)sys->delay_frames * sys->channels * sizeof(float));
    if (sys->channel_ring) memset(sys->channel_ring, 0, (size_t)sys->channel_frames * sys->channels * sizeof(float));
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
    if (readable && config.band_count == 10 && !config.effects.flags) { UnmapViewOfFile(shared); CloseHandle(mapping); return VLC_EGENERIC; }
    if (!readable || (!config.enabled && !config.effects.flags) || (config.band_count == 10 && !config.effects.flags) || !channels || channels > DSP_CHANNELS || rate < 8000 || rate > 768000 || ((config.effects.flags & DSP_REAL_EFFECTS) && (channels > 8 || rate > 192000))) {
        if (readable && config.enabled) InterlockedExchange(&shared->status.error, 3);
        UnmapViewOfFile(shared); CloseHandle(mapping); return VLC_EGENERIC;
    }
    filter_sys_t *sys = calloc(1, sizeof(*sys));
    if (!sys) { UnmapViewOfFile(shared); CloseHandle(mapping); return VLC_ENOMEM; }
    sys->mapping = mapping; sys->shared = shared; sys->channels = channels; sys->rate = rate;
    if (config.effects.flags & DSP_REAL_EFFECTS) {
        sys->delay_frames = (unsigned)(rate * 1.6) + 4; sys->channel_frames = rate / 4 + 4;
        sys->delay_ring = calloc((size_t)sys->delay_frames * channels, sizeof(float)); sys->channel_ring = calloc((size_t)sys->channel_frames * channels, sizeof(float));
        if (!sys->delay_ring || !sys->channel_ring) { free(sys->delay_ring); free(sys->channel_ring); free(sys); UnmapViewOfFile(shared); CloseHandle(mapping); return VLC_ENOMEM; }
    }
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
    free(sys->delay_ring); free(sys->channel_ring); UnmapViewOfFile(sys->shared); CloseHandle(sys->mapping); free(sys);
}

vlc_module_begin()
    set_shortname("FluxPlayer DSP")
    set_description("FluxPlayer native 15/20/31-band equalizer")
    set_capability("audio filter", 1000)
    add_shortcut("equalizer")
    add_string("fluxdsp-map", NULL, "Control map", "FluxPlayer shared control map", true)
        change_private()
    set_callbacks(Open, Close)
vlc_module_end()
