## Diagnosing Evaluation Performance with BinlogMCP

### Step 1: Get the evaluated project view

Call `GetEvaluatedProject` with the binlog path and project name. This returns the flattened project view showing final properties, items, and imports after evaluation.

### Step 2: Get the import chain

Call `GetImportChain` to see the full import hierarchy of `.props` and `.targets` files. Look for:
- Import depth > 20 levels
- Large numbers of imported files
- Unexpected imports from NuGet packages

### Step 3: Check properties

Call `GetProperties` to see all evaluated properties. Filter for `DefaultItemExcludes`, `EnableDefaultItems`, and glob-related properties.

### Step 4: Check items

Call `GetItems` to see all evaluated items. Look for unexpectedly large `Compile` or `None` item groups that suggest overly broad glob patterns.

### Step 5: Trace specific properties (if needed)

Call `TraceProperty(property="DefaultItemExcludes")` to see how a property was set through the import chain — which file set it, in what order, and what the final value is.
