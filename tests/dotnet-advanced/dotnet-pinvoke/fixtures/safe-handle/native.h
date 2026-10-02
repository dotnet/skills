typedef struct resource_handle resource_handle;

resource_handle* open_resource(void);
int use_resource(resource_handle* resource);
void close_resource(resource_handle* resource);
