# Temporary keyboard

## Register

product

## Users and purpose

StarPie users, including CAD and other professional software users, open a temporary keyboard from their wheel for short mouse-driven input. They need numbers, letters, function keys, a full keyboard and named custom key layouts without moving application focus. Custom layouts default to three columns and four rows, with adjustable dimensions.

## Confirmed direction

The user confirmed invocation works and requests matching preview/runtime layouts, responsive equal key areas and ellipsized long labels. Custom layouts contain keyboard keys copied or dragged from a full keyboard template below the canvas. General plugin and host actions belong to a separate host action panel; legacy action files remain available for explicit import there. Existing project rules prioritize predictable interaction, latency and low memory use.

## Design principles

- Make the keyboard itself the editor: select a visible key and edit its properties.
- Drag configured keys to swap positions; provide click alternatives and undo.
- Keep scheme navigation separate from key properties and preview.
- Empty and unavailable states explain how to proceed; never silently execute actions while configuring.
- Preserve saved bindings, parameters and damaged-source recovery.

## Accessibility and boundaries

Use the host's palette, readable system fonts, clear selected/focus/disabled states, keyboard navigation in the activating editor and four-language labels. No decorative animation or heavyweight UI runtime. The actual input window must not activate; Esc and genuine target changes close its session. No permission or input safety is removed to keep the window visible.
