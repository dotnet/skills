package com.example;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Disabled;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Random;

import static org.junit.jupiter.api.Assertions.*;

public class CatalogTest {

    private Catalog catalog;
    private Random rng;

    @BeforeEach
    void setUp() {
        catalog = new Catalog();
        rng = new Random();
    }

    // Smell: Conditional Test Logic — branching inside the test
    @Test
    void addConditionalLogic() {
        String[] skus = {"a", "b", "c"};
        for (String sku : skus) {
            if (sku.equals("b")) {
                catalog.add(sku, 10);
            } else {
                catalog.add(sku, 1);
            }
        }
        assertEquals(12, catalog.quantity("a") + catalog.quantity("b") + catalog.quantity("c"));
    }

    // Smell: Mystery Guest — reads from the file system
    @Test
    void addLoadsFromFile() throws Exception {
        Path tmp = Files.createTempFile("catalog", ".txt");
        Files.writeString(tmp, "x:7");
        String[] parts = Files.readString(tmp).split(":");
        catalog.add(parts[0], Integer.parseInt(parts[1]));
        assertTrue(catalog.inStock("x"));
    }

    // Smell: Sleepy Test
    @Test
    void addSleepy() throws Exception {
        catalog.add("a", 1);
        Thread.sleep(200);
        assertEquals(1, catalog.quantity("a"));
    }

    // Smell: Assertion-Free Test
    @Test
    void addAndDoNothing() {
        catalog.add("a", 1);
    }

    // Smell: Eager Test — calls many distinct methods
    @Test
    void doEverythingAtOnce() {
        catalog.add("a", 1);
        catalog.add("b", 2);
        catalog.add("c", 3);
        catalog.quantity("a");
        catalog.quantity("b");
        catalog.inStock("c");
        catalog.toString();
        assertEquals(6, catalog.quantity("a") + catalog.quantity("b") + catalog.quantity("c"));
    }

    // Smell: Magic Number Test — what does 42 mean?
    @Test
    void quantityAfterAdds() {
        catalog.add("a", 41);
        catalog.add("a", 1);
        assertEquals(42, catalog.quantity("a"));
    }

    // Smell: Sensitive Equality — relies on toString format
    @Test
    void toStringFormat() {
        catalog.add("a", 1);
        assertEquals("Catalog(items=1)", catalog.toString());
    }

    // Smell: Exception Handling in Tests — manual try/catch instead of assertThrows
    @Test
    void addNullSkuManualCatch() {
        try {
            catalog.add(null, 1);
            fail("should have thrown");
        } catch (Exception ex) {
            assertNotNull(ex.getMessage());
        }
    }

    // Smell: General Fixture — rng never used by this test, set up by @BeforeEach
    @Test
    void quantityZeroOnUnknownSku() {
        assertEquals(0, catalog.quantity("unknown"));
    }

    // Smell: Ignored Test
    @Test
    @Disabled("flaky, fix later")
    void disabledTest() {
        catalog.add("x", 1);
        assertEquals(1, catalog.quantity("x"));
    }

    // Well-written test for contrast
    @Test
    void addThrowsOnNegativeCount() {
        IllegalArgumentException ex = assertThrows(
            IllegalArgumentException.class,
            () -> catalog.add("a", -1));
        assertTrue(ex.getMessage().contains("non-negative"));
    }
}
