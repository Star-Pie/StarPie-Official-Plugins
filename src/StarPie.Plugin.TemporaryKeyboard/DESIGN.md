# Temporary keyboard design

The plugin follows StarPie's current WPF palette through InputPanelAppearance. Background, surface, border, foreground and accent are read from the host; accent indicates current key, primary action and drop target. Fonts use Segoe UI / Microsoft YaHei UI at the existing 12–14 DIP scale.

The configuration window has a compact scheme strip, a central keyboard canvas and a selected-key inspector. The canvas uses the same key label and geometry decisions as the input keyboard, with a clear selection outline. Native labelled controls retain visible focus and disabled feedback. The editor has no entrance animation.

Use 8/12/16 DIP spacing, 6 DIP corner radius, one border per surface and no large shadows or decorative gradients. Constrain the custom canvas to the viewport so all cells grow or shrink equally. Trim long labels on one line, with the complete label in a tooltip. The inspector can scroll; the canvas has no unbounded horizontal scroller. Put the full keyboard template below the canvas. Preserve dark/light contrast through the host palette rather than fixed pale gray controls.
