# ImGui, ImNodes & ImGuizmo Lua API

## ImGui API

Use in `re.on_draw_ui` callback. `begin_window`/`end_window` also work in `re.on_frame`.

### Windows
- `imgui.begin_window(name, open, flags)` / `imgui.end_window()` - Only in `on_frame`
- `imgui.begin_child_window(size, border, flags)` / `imgui.end_child_window()`

### Layout
- `imgui.begin_group()` / `imgui.end_group()`
- `imgui.begin_rect()` / `imgui.end_rect(additional_size, rounding)`
- `imgui.begin_disabled(disabled)` / `imgui.end_disabled()`
- `imgui.spacing()`, `imgui.new_line()`, `imgui.same_line()`
- `imgui.indent(width)` / `imgui.unindent(width)`

### Buttons
- `imgui.button(label, size)` - Returns true when pressed
- `imgui.small_button(label)`
- `imgui.invisible_button(id, size, flags)`
- `imgui.arrow_button(id, dir)` - dir uses ImguiDir enum

### Input Widgets
- `imgui.checkbox(label, value)` -> `changed, value`
- `imgui.drag_float(label, value, speed, min, max, format)` -> `changed, value`
- `imgui.drag_float2/3/4(...)` - Vector2f/3f/4f variants
- `imgui.drag_int(label, value, speed, min, max, format)` -> `changed, value`
- `imgui.slider_float(label, value, min, max, format)` -> `changed, value`
- `imgui.slider_int(label, value, min, max, format)` -> `changed, value`
- `imgui.input_text(label, value, flags)` -> `changed, value, sel_start, sel_end`
- `imgui.input_text_multiline(label, value, size, flags)` -> same
- `imgui.combo(label, selection, values_table)` -> `changed, value`

### Text
- `imgui.text(text)` - Plain text
- `imgui.text_colored(text, color)` - Colored text (ARGB)
- `imgui.calc_text_size(text)` -> Vector2f

### Color
- `imgui.color_picker(label, color, flags)` -> `changed, value` (ABGR)
- `imgui.color_picker_argb(label, color, flags)` -> `changed, value` (ARGB)
- `imgui.color_picker3/4(label, vec, flags)` -> `changed, value` (Vector3f/4f)
- `imgui.color_edit(label, color, flags)` -> same variants as picker

### Trees & Headers
- `imgui.tree_node(label)` / `imgui.tree_pop()` - Must pair
- `imgui.tree_node_ptr_id(id, label)` / `imgui.tree_node_str_id(id, label)`
- `imgui.collapsing_header(name)` - Collapsible header
- `imgui.set_next_item_open(is_open, condition)` - ImGuiCond enum

### Progress
- `imgui.progress_bar(progress, size, overlay)` - progress 0.0-1.0

### Fonts
- `imgui.load_font(filepath, size, ranges)` - From `reframework/fonts/`. nil = default font
- `imgui.push_font(font)` / `imgui.pop_font()`
- `imgui.get_default_font_size()`

### Window Info
- `imgui.get_window_size()` / `imgui.get_window_pos()` -> Vector2f
- `imgui.set_next_window_pos(pos, condition, pivot)`
- `imgui.set_next_window_size(size, condition)`
- `imgui.get_display_size()` -> Vector2f

### Cursor
- `imgui.get_cursor_pos()` / `imgui.set_cursor_pos(pos)`
- `imgui.get_cursor_start_pos()` / `imgui.get_cursor_screen_pos()` / `imgui.set_cursor_screen_pos(pos)`

### ID Stack
- `imgui.push_id(id)` / `imgui.pop_id()` / `imgui.get_id()`

### Item State
- `imgui.is_item_hovered(flags)`, `imgui.is_item_active()`, `imgui.is_item_focused()`

### Keyboard & Mouse
- `imgui.get_key_index(imgui_key)`, `imgui.is_key_down(key)`, `imgui.is_key_pressed(key)`, `imgui.is_key_released(key)`
- `imgui.get_mouse()` -> Vector2f
- `imgui.is_mouse_down(btn)`, `imgui.is_mouse_clicked(btn)`, `imgui.is_mouse_released(btn)`, `imgui.is_mouse_double_clicked(btn)`

### Drawing Primitives
- `imgui.draw_list_path_clear()` / `imgui.draw_list_path_line_to(pos)` / `imgui.draw_list_path_stroke(color, closed, thickness)`

### Tooltips
- `imgui.begin_tooltip()` / `imgui.end_tooltip()` / `imgui.set_tooltip(text)`

### Popups
- `imgui.open_popup(str_id, flags)` / `imgui.begin_popup(str_id, flags)` / `imgui.end_popup()`
- `imgui.begin_popup_context_item(str_id, flags)` / `imgui.close_current_popup()` / `imgui.is_popup_open(str_id)`

### Item Width
- `imgui.push_item_width(w)` / `imgui.pop_item_width()` / `imgui.set_next_item_width(w)` / `imgui.calc_item_width()`

### Style
- `imgui.push_style_color(style_color, color)` / `imgui.pop_style_color(count)`
- `imgui.push_style_var(idx, value)` / `imgui.pop_style_var(count)`

### Lists
- `imgui.begin_list_box(label, size)` / `imgui.end_list_box()`

### Menus
- `imgui.begin_menu_bar()` / `imgui.end_menu_bar()`
- `imgui.begin_main_menu_bar()` / `imgui.end_main_menu_bar()`
- `imgui.begin_menu(label, enabled)` / `imgui.end_menu()`
- `imgui.menu_item(label, shortcut, selected, enabled)`

### Scrolling
- `imgui.get_scroll_x/y()` / `imgui.set_scroll_x/y(val)`
- `imgui.get_scroll_max_x/y()` / `imgui.set_scroll_here_x/y(ratio)` / `imgui.set_scroll_from_pos_x/y(pos, ratio)`

### Tables
- `imgui.begin_table(str_id, columns, flags, outer_size, inner_width)` / `imgui.end_table()`
- `imgui.table_next_row(flags, min_height)` / `imgui.table_next_column()` / `imgui.table_set_column_index(idx)`
- `imgui.table_setup_column(label, flags, width, user_id)` / `imgui.table_setup_scroll_freeze(cols, rows)`
- `imgui.table_headers_row()` / `imgui.table_header(label)`
- `imgui.table_get_sort_specs()`, `table_get_column_count/index()`, `table_get_row_index()`
- `imgui.table_set_bg_color(target, color, column)`

### Enums
- `ImGuiCond`: None, Always, Once, FirstUseEver, Appearing
- `ImGuiWindowFlags`, `ImGuiStyleVar`, `ImGuiTableFlags`, `ImGuiColorEditFlags`

---

## ImNodes API

Node editor library. Use in `re.on_frame`.

### Editor: `imnodes.begin_node_editor()` / `end_node_editor()` / `editor_move_to_node(id)` / `editor_reset_panning()` / `editor_get_panning()`
### Nodes: `begin_node(id)` / `end_node()` / `begin_node_titlebar()` / `end_node_titlebar()`
### Attributes: `begin_input_attribute(id)` / `begin_output_attribute(id)` / `begin_static_attribute(id)` + end variants
### Links: `link(id, start, end)` / `is_link_started/dropped/created/destroyed/hovered/selected()`
### Styling: `push_color_style(item, color)` / `pop_color_style()` / `push_style_var(var, val)` / `pop_style_var()`
### Selection: `num_selected_nodes/links()` / `get_selected_nodes/links()` / `select_node/link(id)` / `clear_node/link_selection()`

---

## ImGuizmo API

- `imguizmo.is_over()` - Is any gizmo hovered?
- `imguizmo.is_using()` - Is any gizmo being edited?
