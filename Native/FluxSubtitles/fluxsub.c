/* SPDX-License-Identifier: GPL-3.0-only
 * Decoded subtitle bitmaps -> bounded shared memory -> WPF compositor.
 * Video frames and hardware decoder surfaces are never touched by this filter.
 */
#define MODULE_STRING "fluxsub"
#define __PLUGIN__
#define DLL_EXPORT
#define _CRT_NONSTDC_NO_DEPRECATE
#include <stdint.h>
#include <winsock2.h>
#include <windows.h>
#include <stddef.h>
typedef intptr_t ssize_t;
#define poll WSAPoll
#include <vlc_common.h>
#include <vlc_plugin.h>
#include <vlc_filter.h>
#include <vlc_modules.h>
#include <vlc_image.h>
#include <vlc_subpicture.h>
#include <vlc_text_style.h>
#include <vlc/libvlc.h>

#define CUES 8
#define REGIONS 16
#define PIXELS (8u * 1024u * 1024u)
#define SLOT (PIXELS + 1024u)
#define MAP_BYTES (4096u + CUES * SLOT)
#define MAGIC 0x46585350u
typedef struct { int32_t x, y, width, height, align, alpha; uint32_t offset, length; } region_meta;
typedef struct {
    volatile LONG sequence, alive;
    int64_t order, stop;
    int32_t width, height, count, bytes;
    uint32_t hash;
    region_meta regions[REGIONS];
} cue_meta;
_Static_assert(sizeof(region_meta) == 32, "Region ABI");
_Static_assert(offsetof(cue_meta, regions) == 44, "Cue ABI");
struct filter_sys_t {
    HANDLE mapping;
    uint8_t *data;
    volatile LONG refs, slots[CUES];
    CRITICAL_SECTION gate;
    filter_t *text;
    image_handler_t *images;
};
typedef struct { struct filter_sys_t *bridge; subpicture_t *original; int slot; } proxy;
static cue_meta *Cue(struct filter_sys_t *b, int slot) { return (cue_meta *)(b->data + 4096u + (unsigned)slot * SLOT); }
static void Begin(cue_meta *c) { InterlockedIncrement(&c->sequence); MemoryBarrier(); }
static void End(cue_meta *c) { MemoryBarrier(); InterlockedIncrement(&c->sequence); }
static void Release(struct filter_sys_t *b)
{
    if (InterlockedDecrement(&b->refs) != 0) return;
    if (b->text) { if (b->text->p_module) module_unneed(b->text, b->text->p_module); vlc_object_release(b->text); }
    if (b->images) image_HandlerDelete(b->images);
    UnmapViewOfFile(b->data); CloseHandle(b->mapping); DeleteCriticalSection(&b->gate); free(b);
}
static uint32_t Hash(uint32_t hash, const uint8_t *bytes, size_t count)
{ for (size_t i = 0; i < count; ++i) hash = (hash ^ bytes[i]) * 16777619u; return hash; }
static void Capture(proxy *p, subpicture_t *spu, const video_format_t *src, const video_format_t *dst, vlc_tick_t time)
{
    struct filter_sys_t *b = p->bridge; EnterCriticalSection(&b->gate);
    if (p->slot < 0) {
        for (int i = 0; i < CUES; ++i) if (InterlockedCompareExchange(&b->slots[i], 1, 0) == 0) { p->slot = i; break; }
        if (p->slot < 0) { InterlockedExchange((LONG *)(b->data + 12), 4); LeaveCriticalSection(&b->gate); return; }
    }
    cue_meta *cue = Cue(b, p->slot); Begin(cue); cue->alive = 0; cue->count = cue->bytes = 0;
    cue->order = spu->i_order;
    cue->width = spu->i_original_picture_width > 0 ? spu->i_original_picture_width : (int)src->i_visible_width;
    cue->height = spu->i_original_picture_height > 0 ? spu->i_original_picture_height : (int)src->i_visible_height;
    if (cue->width <= 0) cue->width = (int)dst->i_visible_width;
    if (cue->height <= 0) cue->height = (int)dst->i_visible_height;
    int64_t remaining = spu->i_stop > time ? (spu->i_stop - time) / 1000 : 250;
    if (remaining > 86400000) remaining = 86400000;
    cue->stop = (int64_t)GetTickCount64() + remaining;
    uint8_t *payload = (uint8_t *)cue + 1024; uint32_t hash = 2166136261u;
    b->text->fmt_out.video = *dst;
    for (subpicture_region_t *r = spu->p_region; r; r = r->p_next) {
        if (cue->count >= REGIONS) { InterlockedExchange((LONG *)(b->data + 12), 1); break; }
        subpicture_region_t *copy = NULL; picture_t *picture = r->p_picture;
        if (r->fmt.i_chroma == VLC_CODEC_TEXT) {
            copy = subpicture_region_New(&r->fmt);
            if (!copy) continue;
            copy->p_text = text_segment_Copy(r->p_text); copy->i_text_align = r->i_text_align;
            copy->i_x = r->i_x; copy->i_y = r->i_y; copy->i_align = r->i_align; copy->b_gridmode = r->b_gridmode;
            copy->b_balanced_text = r->b_balanced_text; copy->b_noregionbg = r->b_noregionbg;
            copy->i_max_width = r->i_max_width; copy->i_max_height = r->i_max_height;
            const vlc_fourcc_t chromas[] = { VLC_CODEC_RGBA, VLC_CODEC_YUVA, 0 };
            var_SetInteger(b->text, "spu-elapsed", time - spu->i_start); var_SetBool(b->text, "text-rerender", false);
            b->text->pf_render(b->text, copy, copy, chromas); picture = copy->p_picture;
        }
        if (!picture) { if (copy) subpicture_region_Delete(copy); continue; }
        video_format_t output; video_format_Init(&output, VLC_CODEC_BGRA);
        output.i_width = output.i_visible_width = picture->format.i_visible_width;
        output.i_height = output.i_visible_height = picture->format.i_visible_height;
        output.i_sar_num = output.i_sar_den = 1;
        picture_t *rgba = image_Convert(b->images, picture, &picture->format, &output);
        if (rgba) {
            unsigned width = rgba->format.i_visible_width, height = rgba->format.i_visible_height;
            size_t length = (size_t)width * height * 4;
            if (width && height && width <= 4096 && height <= 2160 && length <= PIXELS - (unsigned)cue->bytes && rgba->p[0].i_pixel_pitch == 4) {
                region_meta *m = &cue->regions[cue->count++];
                m->x = copy ? copy->i_x : r->i_x; m->y = copy ? copy->i_y : r->i_y; m->width = (int32_t)width; m->height = (int32_t)height;
                m->align = spu->b_absolute ? 0 : copy ? copy->i_align : r->i_align;
                int opacity = 255;
                if (spu->b_fade && spu->i_stop > spu->i_start) {
                    double last_quarter = (spu->i_stop - spu->i_start) / 4.0;
                    double remaining_ticks = (double)(spu->i_stop - time);
                    if (remaining_ticks < last_quarter) opacity = remaining_ticks > 0 ? (int)(255.0 * remaining_ticks / last_quarter) : 0;
                }
                m->alpha = opacity * spu->i_alpha * r->i_alpha / 65025;
                m->offset = (uint32_t)cue->bytes; m->length = (uint32_t)length;
                for (unsigned y = 0; y < height; ++y) {
                    const uint8_t *row = rgba->p[0].p_pixels + (y + rgba->format.i_y_offset) * rgba->p[0].i_pitch + rgba->format.i_x_offset * 4;
                    memcpy(payload + cue->bytes + y * width * 4, row, (size_t)width * 4);
                }
                hash = Hash(hash, (const uint8_t *)m, sizeof(*m)); hash = Hash(hash, payload + cue->bytes, length);
                cue->bytes += (int32_t)length;
            } else InterlockedExchange((LONG *)(b->data + 12), 2);
            picture_Release(rgba);
        } else InterlockedExchange((LONG *)(b->data + 12), 3);
        video_format_Clean(&output); if (copy) subpicture_region_Delete(copy);
    }
    cue->hash = hash; cue->alive = 1; End(cue); LeaveCriticalSection(&b->gate);
}
static int Validate(subpicture_t *spu, bool sc, const video_format_t *src, bool dc, const video_format_t *dst, vlc_tick_t time)
{ VLC_UNUSED(spu); VLC_UNUSED(sc); VLC_UNUSED(src); VLC_UNUSED(dc); VLC_UNUSED(dst); VLC_UNUSED(time); return VLC_EGENERIC; }
static void Update(subpicture_t *spu, const video_format_t *src, const video_format_t *dst, vlc_tick_t time)
{
    proxy *p = (proxy *)spu->updater.p_sys;
    p->original->i_start = spu->i_start; p->original->i_stop = spu->i_stop;
    subpicture_Update(p->original, src, dst, time); Capture(p, p->original, src, dst, time);
}
static void Destroy(subpicture_t *spu)
{
    proxy *p = (proxy *)spu->updater.p_sys; struct filter_sys_t *b = p->bridge;
    EnterCriticalSection(&b->gate);
    if (p->slot >= 0) { cue_meta *c = Cue(b, p->slot); Begin(c); c->alive = 0; End(c); InterlockedExchange(&b->slots[p->slot], 0); }
    LeaveCriticalSection(&b->gate);
    subpicture_Delete(p->original); free(p); Release(b);
}
static subpicture_t *Filter(filter_t *filter, subpicture_t *original)
{
    if (!original || !original->b_subtitle) return original;
    struct filter_sys_t *b = filter->p_sys;
    proxy *p = calloc(1, sizeof(*p));
    if (!p) return original;
    p->bridge = b; p->original = original; p->slot = -1;
    subpicture_updater_t updater = { Validate, Update, Destroy, (subpicture_updater_sys_t *)p };
    subpicture_t *spu = subpicture_New(&updater);
    if (!spu) { free(p); return original; }
    InterlockedIncrement(&b->refs);
    spu->i_channel = original->i_channel; spu->i_order = original->i_order;
    spu->i_start = original->i_start; spu->i_stop = original->i_stop;
    spu->b_ephemer = original->b_ephemer; spu->b_fade = original->b_fade;
    spu->b_subtitle = original->b_subtitle; spu->b_absolute = original->b_absolute;
    spu->i_original_picture_width = original->i_original_picture_width;
    spu->i_original_picture_height = original->i_original_picture_height; spu->i_alpha = original->i_alpha;
    return spu;
}
static int Open(vlc_object_t *object)
{
    filter_t *filter = (filter_t *)object; char *name = var_InheritString(filter, "fluxsub-map");
    if (!name) return VLC_EGENERIC;
    struct filter_sys_t *b = calloc(1, sizeof(*b)); if (!b) { libvlc_free(name); return VLC_ENOMEM; }
    b->mapping = OpenFileMappingA(FILE_MAP_ALL_ACCESS, FALSE, name); libvlc_free(name);
    if (!b->mapping) { free(b); return VLC_EGENERIC; }
    b->data = MapViewOfFile(b->mapping, FILE_MAP_ALL_ACCESS, 0, 0, MAP_BYTES);
    if (!b->data || *(uint32_t *)b->data != MAGIC || *(uint32_t *)(b->data + 4) != 1) { if (b->data) UnmapViewOfFile(b->data); CloseHandle(b->mapping); free(b); return VLC_EGENERIC; }
    b->refs = 1; InitializeCriticalSection(&b->gate);
    b->text = vlc_object_create(filter, sizeof(*b->text));
    if (b->text) {
        es_format_Init(&b->text->fmt_in, VIDEO_ES, 0); es_format_Init(&b->text->fmt_out, VIDEO_ES, 0);
        b->text->p_module = module_need(b->text, "text renderer", NULL, false);
        var_Create(b->text, "spu-elapsed", VLC_VAR_INTEGER); var_Create(b->text, "text-rerender", VLC_VAR_BOOL);
    }
    b->images = image_HandlerCreate(filter);
    if (!b->text || !b->text->p_module || !b->images) { InterlockedExchange((LONG *)(b->data + 12), 5); Release(b); return VLC_EGENERIC; }
    filter->p_sys = b; filter->pf_sub_filter = Filter; InterlockedIncrement((LONG *)(b->data + 8)); return VLC_SUCCESS;
}
static void Close(vlc_object_t *object)
{ struct filter_sys_t *b = ((filter_t *)object)->p_sys; InterlockedDecrement((LONG *)(b->data + 8)); Release(b); }
vlc_module_begin()
    set_shortname("Flux subtitles")
    set_description("WPF subtitle bitmap composition bridge")
    set_capability("sub filter", 0)
    set_callbacks(Open, Close)
    add_shortcut("fluxsub")
    add_string("fluxsub-map", "", "Shared subtitle mapping", "Shared subtitle mapping", false)
vlc_module_end()
