package com.example;

import java.util.HashMap;
import java.util.Map;

public class Catalog {
    private final Map<String, Integer> stock = new HashMap<>();

    public void add(String sku, int count) {
        if (sku == null || sku.isEmpty()) {
            throw new IllegalArgumentException("sku is required");
        }
        if (count < 0) {
            throw new IllegalArgumentException("count must be non-negative");
        }
        stock.merge(sku, count, Integer::sum);
    }

    public int quantity(String sku) {
        return stock.getOrDefault(sku, 0);
    }

    public boolean inStock(String sku) {
        return quantity(sku) > 0;
    }

    @Override
    public String toString() {
        return "Catalog(items=" + stock.size() + ")";
    }
}
