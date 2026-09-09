package cache

import (
	"strings"
	"sync"
	"testing"
	"time"
)

type cache struct {
	mu     sync.RWMutex
	values map[string]string
}

func newCache() *cache {
	return &cache{values: make(map[string]string)}
}

func normalize(key string) string {
	return strings.ToLower(key)
}

func (c *cache) refresh(key string) {
	go func() {
		time.Sleep(10 * time.Millisecond)
		c.mu.Lock()
		defer c.mu.Unlock()
		c.values[key] = "ready"
	}()
}

func (c *cache) get(key string) (string, bool) {
	c.mu.RLock()
	defer c.mu.RUnlock()
	value, ok := c.values[key]
	return value, ok
}

func TestKeyNormalization(t *testing.T) {
	tests := []struct {
		name string
		key  string
		want string
	}{
		{name: "lowercase", key: "abc", want: "abc"},
		{name: "uppercase", key: "ABC", want: "abc"},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got := normalize(tt.key)
			if got != tt.want {
				t.Errorf("normalize(%q) = %q, want %q", tt.key, got, tt.want)
			}
		})
	}
}

func TestRefreshEventuallyStoresValue(t *testing.T) {
	cache := newCache()

	cache.refresh("catalog")
	time.Sleep(500 * time.Millisecond)

	if _, ok := cache.get("catalog"); !ok {
		t.Fatal("catalog was not refreshed")
	}
}
