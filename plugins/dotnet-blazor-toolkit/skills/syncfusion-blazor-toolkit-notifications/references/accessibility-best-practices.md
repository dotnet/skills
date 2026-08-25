# Accessibility & Best Practices

## Table of Contents

- [Accessibility Overview](#accessibility-overview)
- [Spinner Accessibility](#spinner-accessibility)
- [WCAG 2.1 AA Compliance](#wcag-21-aa-compliance)
- [Keyboard Navigation](#keyboard-navigation)
- [Semantic HTML](#semantic-html)
- [Best Practices](#best-practices)
- [Testing Accessibility](#testing-accessibility)
- [Common Accessibility Mistakes](#common-accessibility-mistakes)
- [Accessibility Resources](#accessibility-resources)
- [Summary: Accessibility Checklist](#summary-accessibility-checklist)

---

## Accessibility Overview

The Spinner component is designed with accessibility in mind, supporting WCAG 2.1 AA compliance standards.

---

## Spinner Accessibility

### 1. Label for Screen Readers

Spinners display text that's announced to screen readers:

```csharp
<!-- ✅ Clear label for accessibility -->
<SfSpinner Visible="true" Label="Loading data from server"></SfSpinner>

<!-- ⚠️ Generic label - could be more specific -->
<SfSpinner Visible="true" Label="Loading"></SfSpinner>

<!-- ❌ No label -->
<SfSpinner Visible="true" Label=""></SfSpinner>
```

### 2. ARIA Attributes

The Spinner component automatically includes appropriate ARIA attributes:

```csharp
<!-- With explicit label - aria-label uses the Label value -->
<SfSpinner Label="Processing your request" 
           Visible="true">
</SfSpinner>

<!-- Without label - aria-label defaults to "Loading" -->
<SfSpinner Visible="true">
</SfSpinner>

<!-- Renders as: -->
<!-- <div role="status" aria-busy="true" aria-label="Processing your request"> -->
```

**Default aria-label**: When no `Label` is provided, the spinner's `aria-label` automatically defaults to `"Loading"` for accessibility compliance.

### 3. Status Region

Spinners act as status regions, automatically announcing state changes:

```csharp
<div class="container">
	<!-- Status region that announces loading state -->
	@if (isLoading)
	{
		<SfSpinner Visible="true" Label="Fetching data..."></SfSpinner>
	}
	else
	{
		<div role="status">Data loaded successfully</div>
	}
</div>

@code {
	private bool isLoading = true;

	protected override async Task OnInitializedAsync()
	{
		// Simulate data fetching
		await Task.Delay(2000);
		isLoading = false;
	}
}
```

---

## WCAG 2.1 AA Compliance

### Color Contrast

Ensure sufficient color contrast between spinner and background:

```csharp
<!-- ✅ Good contrast -->
<div style="background: white;">
	<SfSpinner Visible="true" Label="Loading..."></SfSpinner>
</div>

<!-- ⚠️ Poor contrast - may fail WCAG -->
<div style="background: #f5f5f5;">
	<SfSpinner Visible="true" Label="Loading..."></SfSpinner>
</div>
```

---

## Keyboard Navigation

### Spinner in Modal Dialogs

Ensure proper focus management:

```csharp
<div class="modal" role="dialog" aria-labelledby="modal-title">
	<h2 id="modal-title">Processing</h2>
	
	@if (isProcessing)
	{
		<SfSpinner Visible="true" Label="Please wait..."></SfSpinner>
	}
	
	<!-- Buttons only enabled when processing complete -->
	<button disabled="@isProcessing" @onclick="@Confirm">Confirm</button>
	<button disabled="@isProcessing" @onclick="@Cancel">Cancel</button>
</div>

@code {
	private bool isProcessing = true;

	private async Task Confirm()
	{
		isProcessing = true;
		// Simulate confirmation processing
		await Task.Delay(2000);
		isProcessing = false;
	}

	private async Task Cancel()
	{
		isProcessing = true;
		// Simulate cancellation processing
		await Task.Delay(1500);
		isProcessing = false;
	}
}
```

### Tab Focus Management

```csharp
<div>
	<!-- Skip loading UI during loading -->
	@if (!isLoading)
	{
		<form>
			<input type="text" placeholder="Name" />
			<button type="submit">Submit</button>
		</form>
	}
	else
	{
		<SfSpinner Visible="true" Label="Processing form..."></SfSpinner>
	}
</div>

@code {
	private bool isLoading = true;
}
```

---

## Semantic HTML

### Proper Container Structure

```csharp
<!-- ✅ Semantic and accessible structure -->
<section aria-label="Loading">
	<header>
		<h1>Loading Profile</h1>
	</header>
	
	<article>
		<p>
			<SfSpinner Visible="true" Label="Loading profile data"></SfSpinner>
		</p>
	</article>
</section>

<!-- ❌ Generic div without semantic meaning -->
<div>
	<SfSpinner Visible="true"></SfSpinner>
</div>
```

---

## Best Practices

### Practice 1: Always Provide Context

```csharp
<!-- ✅ Clear context -->
<div aria-label="Loading user list">
	<SfSpinner Visible="true" Label="Loading user list..."></SfSpinner>
</div>

<!-- ❌ No context -->
<SfSpinner Visible="true"></SfSpinner>
```

### Practice 2: Timeout Handling

```csharp
@if (!contentLoaded)
{
	@if (!loadTimeout)
	{
		<SfSpinner Visible="true" Label="Loading content..."></SfSpinner>
	}
	else
	{
		<!-- Show error state after timeout -->
		<div role="alert" class="alert alert-warning">
			<p>Content is taking longer than expected to load.</p>
			<button @onclick="@Retry">Retry</button>
		</div>
	}
}
else
{
	<div>Content loaded successfully</div>
}

@code {
	private bool contentLoaded = false;
	private bool loadTimeout = false;

	protected override async Task OnInitializedAsync()
	{
		using (var cts = new System.Threading.CancellationTokenSource(5000))
		{
			try
			{
				await LoadContent(cts.Token);
				contentLoaded = true;
			}
			catch (OperationCanceledException)
			{
				loadTimeout = true;
			}
		}
	}

	private async Task LoadContent(System.Threading.CancellationToken token)
	{
		await Task.Delay(3000, token);
	}

	private async Task Retry()
	{
		contentLoaded = false;
		loadTimeout = false;
		await OnInitializedAsync();
	}
}
```

---

## Testing Accessibility

### Manual Testing Checklist

- [ ] **Screen Reader Testing:** Test with NVDA, JAWS, or VoiceOver
  - Verify spinner messages are announced
  - Check for redundant announcements

- [ ] **Keyboard Navigation:** 
  - Can users navigate with Tab key?
  - Are focus indicators visible?
  - Can users close/interact with loading states?

- [ ] **Color Contrast:**
  - Verify 4.5:1 minimum contrast for text
  - Verify 3:1 contrast for UI components
  - Test with contrast checker tools

- [ ] **Zoom and Scaling:**
  - Test at 200% zoom level
  - Verify no horizontal scrolling introduced
  - Check responsive behavior

### Automated Testing

```csharp
// Example using Playwright for accessibility testing
[Test]
public async Task SpinnerIsAccessible()
{
	// Verify spinner has correct role attribute on root element
	// Note: .e-spinner-pane is the root element, not .e-spinner
	var spinnerPane = page.Locator(".e-spinner-pane");
	var role = await spinnerPane.GetAttributeAsync("role");
	Assert.That(role, Is.EqualTo("status"));

	// The role="status" attribute provides implicit aria-live="polite" behavior
	// per WAI-ARIA specifications, so an explicit aria-live attribute is not needed.
	// The aria-busy attribute indicates the loading state
	var ariaBusy = await spinnerPane.GetAttributeAsync("aria-busy");
	Assert.That(ariaBusy, Is.EqualTo("true"));
}
```

---

## Common Accessibility Mistakes

### ❌ Mistake 1: No Labels
```csharp
<!-- Don't do this -->
<SfSpinner Visible="true"></SfSpinner>

<!-- Do this instead -->
<SfSpinner Visible="true" Label="Processing request"></SfSpinner>
```

### ❌ Mistake 2: Indefinite Loading States
```csharp
<!-- Don't leave users waiting indefinitely -->
@if (isLoading)
{
	<SfSpinner Visible="true" Label="Loading..."></SfSpinner>
}

<!-- Do set timeouts and show error states -->
@if (isLoading)
{
	<SfSpinner Visible="true" Label="Loading..."></SfSpinner>
}
else if (loadError)
{
	<div role="alert">Failed to load content. <button>Retry</button></div>
}
```

### ❌ Mistake 3: Insufficient Focus Management
```csharp
<!-- ❌ Don't block interaction during loading -->
<form>
	<input disabled="@isLoading" />
	<button disabled="@isLoading">Submit</button>
	@if (isLoading) { <SfSpinner Visible="true" /> }
</form>

<!-- ✅ Do provide clear feedback -->
<form>
	<input disabled="@isLoading" aria-busy="@isLoading" />
	<button disabled="@isLoading" aria-busy="@isLoading">Submit</button>
	@if (isLoading) { <SfSpinner Visible="true" Label="Submitting..."></SfSpinner> }
</form>
```

---

## Accessibility Resources

- **WCAG 2.1 Guidelines:** https://www.w3.org/WAI/WCAG21/quickref/
- **ARIA Authoring Guide:** https://www.w3.org/WAI/ARIA/apg/
- **Color Contrast Checker:** https://webaim.org/resources/contrastchecker/
- **Accessibility Testing Tools:** NVDA, JAWS, axe DevTools

---

## Summary: Accessibility Checklist

Before deploying loading indicators, verify:

- [ ] All spinners have descriptive text
- [ ] Color contrast meets WCAG AA standards (4.5:1)
- [ ] Keyboard navigation works properly
- [ ] Screen readers can announce loading states
- [ ] Timeouts prevent indefinite loading states
- [ ] Focus management is handled correctly
- [ ] Semantic HTML is used appropriately
- [ ] Testing with assistive technologies completed

---

## Next Steps

- Learn more: Review [Spinner Events & Customization](spinner-events-customization.md)
- Back to implementation: [Spinner Implementation](spinner-implementation.md)
