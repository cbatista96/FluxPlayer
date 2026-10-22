/* SPDX-License-Identifier: GPL-3.0-only
 * Standalone processing-cost probe using the exact production filter functions.
 * No audio device, UI, decoding or real-time sleeping is involved.
 */
#include "fluxdsp.c"

static double CpuMilliseconds(void)
{
    FILETIME creation, exit_time, kernel, user;
    if (!GetThreadTimes(GetCurrentThread(), &creation, &exit_time, &kernel, &user)) return 0.0;
    ULARGE_INTEGER k, u; k.LowPart = kernel.dwLowDateTime; k.HighPart = kernel.dwHighDateTime;
    u.LowPart = user.dwLowDateTime; u.HighPart = user.dwHighDateTime;
    return (double)(k.QuadPart + u.QuadPart) / 10000.0;
}

static double SecondsNow(void)
{
    LARGE_INTEGER value, frequency; QueryPerformanceCounter(&value); QueryPerformanceFrequency(&frequency);
    return (double)value.QuadPart / (double)frequency.QuadPart;
}

static __declspec(noinline) void RunCase(unsigned count, unsigned active)
{
    static const float iso31[] = { 20,25,31.5f,40,50,63,80,100,125,160,200,250,315,400,500,630,
        800,1000,1250,1600,2000,2500,3150,4000,5000,6300,8000,10000,12500,16000,20000 };
    const unsigned frames = 480, blocks = 6000; /* 60 seconds of 48 kHz stereo */
    dsp_shared shared = { 0 }; filter_sys_t *sys = calloc(1, sizeof(*sys));
    if (!sys) exit(2);
    sys->shared = &shared; sys->channels = 2; sys->rate = 48000;
    dsp_config config = { 0 }; config.magic = DSP_MAGIC; config.protocol = DSP_PROTOCOL;
    config.sequence = 2; config.enabled = 1; config.band_count = (int32_t)count;
    config.bandwidth = count == 20 ? (float)(log(1000.0) / log(2.0) / 19.0) : 1.f / 3.f;
    config.anti_clip = 1;
    for (unsigned band = 0; band < count; band++) {
        config.frequencies[band] = count == 31 ? iso31[band] : (float)(20.0 * pow(1000.0, (double)band / (count - 1)));
        config.gains[band] = band < active ? (band & 1 ? -6.f : 6.f) : 0.f;
    }
    memcpy(&shared.config, &config, sizeof(config)); Configure(sys, &config, true);
    float input[960], output[960];
    for (unsigned frame = 0; frame < frames; frame++) {
        float value = (float)(0.1 * sin(2.0 * DSP_PI * 440.0 * frame / 48000.0));
        input[frame * 2] = input[frame * 2 + 1] = value;
    }
    filter_t filter = { 0 }; filter.p_sys = sys;
    block_t block = { 0 }; block.p_buffer = (uint8_t *)output; block.i_buffer = sizeof(output); block.i_nb_samples = frames;
    volatile double checksum = 0.0;
    double cpu_start = CpuMilliseconds(), start = SecondsNow();
    for (unsigned iteration = 0; iteration < blocks; iteration++) {
        memcpy(output, input, sizeof(output)); Filter(&filter, &block);
        checksum += output[iteration % 960];
    }
    double elapsed = SecondsNow() - start, cpu_ms = CpuMilliseconds() - cpu_start;
    printf("bands=%u active=%u equivalent_audio_seconds=60 wall_ms=%.3f thread_cpu_ms=%.3f one_core_percent=%.4f checksum=%.8f\n",
        count, active, elapsed * 1000.0, cpu_ms, cpu_ms / 600.0, checksum);
    double configure_start = SecondsNow(), worst = 0.0;
    for (int iteration = 0; iteration < 100; iteration++) {
        config.gains[0] = iteration & 1 ? 5.f : 6.f;
        double before = SecondsNow(); Configure(sys, &config, false);
        worst = fmax(worst, SecondsNow() - before);
    }
    printf("configuration_updates=100 mean_us=%.3f worst_us=%.3f\n", (SecondsNow() - configure_start) * 10000.0, worst * 1000000.0);
    free(sys);
}

int main(void)
{
    RunCase(31, 0); RunCase(31, 3); RunCase(20, 20); RunCase(31, 31);
    return 0;
}
