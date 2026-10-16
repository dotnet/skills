// The returned pointer owns a UTF-8, NUL-terminated allocation.
// It must be released with widget_free_version.
const char* widget_get_version(void);
void widget_free_version(const char* value);
