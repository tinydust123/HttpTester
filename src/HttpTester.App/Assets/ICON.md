# Http探测器图标

使用内置 image_gen 工具生成；设计为青绿色圆角底板、白色放大镜和青色闪电。

- `HttpDetector.source.png`：保留原始透明背景生成图。
- `HttpDetector.png`：256px 界面图标。
- `HttpDetector.ico`：包含 16、20、24、32、40、48、64、96、128、256px 的 Windows 图标，同时用于 EXE 资源和 WPF 窗口图标。

`python scripts/build_icon.py` 可重新完成尺寸转换（需要 Pillow）；日常编译使用现有资源，不需要 Python 或 Pillow。

生成提示词：

> Use case: logo-brand. Asset type: production Windows desktop application icon for a lightweight HTTP/HTTPS request testing utility named Http探测器. Create exactly ONE finished square icon, not a mockup and not an icon sheet. Design: a simple bold white magnifying glass with a short thick handle angled to the lower right; a single small cyan lightning bolt centered inside the lens represents a fast network probe. The magnifying glass must have a very clear silhouette and generous open lens, thick enough to remain legible at 16x16 and 32x32 pixels. Background within the icon: a softly rounded square tile in deep teal (#087F8C, subtle #075B68 to #149DA6 tonal variation) matching a clean professional Windows utility. Style: crisp, minimal, polished flat geometric app icon with only a very subtle edge highlight, high contrast, restrained detail. Framing: centered, almost full-bleed tile occupying about 94 percent of the square canvas, with transparent pixels outside its rounded corners; balanced optical spacing. Text: NONE. Avoid letters, HTTP text, numbers, extra symbols, browser windows, screenshots, drop shadows outside the icon, photorealism, busy decoration, tiny lines, perspective, watermarks. Output a high-resolution square PNG with genuinely transparent outer background.
