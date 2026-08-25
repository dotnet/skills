# Uploader Component - File Upload and Management

## Table of Contents
- [SfUploader Overview](#sfuploader-overview)
- [Basic File Upload](#basic-file-upload)
- [Preloaded Files](#preloaded-files)
- [Auto Upload Configuration](#auto-upload-configuration)
- [File Validation](#file-validation)
- [Directory and Sequential Upload](#directory-and-sequential-upload)
- [Drag and Drop](#drag-and-drop)
- [UI Customization](#ui-customization)
- [Uploader Templates](#uploader-templates)
- [Upload Events](#upload-events)
- [Progress and Cancellation](#progress-and-cancellation)
- [Error Handling](#error-handling)
- [Practical Examples](#practical-examples)

## SfUploader Overview

**SfUploader** provides a complete file upload solution with validation, progress tracking, and async upload capabilities. It supports single/multiple files, drag-and-drop, and extensive customization.

**When to use SfUploader:**
- Document upload
- Image upload
- Bulk file handling
- File management interfaces
- Profile picture upload

### Uploader Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ID` | `string` | `""` | Unique identifier for the component |
| `AutoUpload` | `bool` | `true` | Upload files immediately after selection |
| `AllowMultiple` | `bool` | `true` | Allow multiple file selection |
| `AllowedExtensions` | `string` | `""` | File extensions to allow (e.g., ".jpg,.png,.pdf") |
| `MaxFileSize` | `double` | `30000000` | Maximum file size in bytes |
| `MinFileSize` | `double` | `0` | Minimum file size in bytes |
| `DirectoryUpload` | `bool` | `false` | Enable folder upload |
| `SequentialUpload` | `bool` | `false` | Upload files sequentially |
| `DropArea` | `string?` | `null` | Custom drop target element selector |
| `DropEffect` | `DropEffect` | `Default` | Drag-drop effect (Copy, Move, Link, None) |
| `CssClass` | `string` | `""` | Custom CSS classes |
| `Disabled` | `bool` | `false` | Disable the uploader |
| `ShowFileList` | `bool` | `true` | Display file list |
| `ShowProgressBar` | `bool` | `true` | Show progress bar during upload |
| `EnableHtmlSanitizer` | `bool` | `true` | Sanitize filenames for XSS |
| `EnablePersistence` | `bool` | `false` | Persist upload state to localStorage using the component's ID |
| `TabIndex` | `int` | `0` | Tab order |
| `HttpClientInstance` | `HttpClient` | `new()` | Custom HttpClient for upload requests |

### Basic Implementation

```razor
<SfUploader AutoUpload="false" AllowedExtensions=".jpg,.png,.pdf"></SfUploader>
```

### With Upload URL

```razor
<SfUploader ID="UploadFiles" AutoUpload="false" AllowedExtensions=".jpg,.png,.pdf">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>
```

## Basic File Upload

### Single File Upload

```razor
<div class="upload-demo">
    <h3>Upload Single File</h3>
    <SfUploader 
        AutoUpload="false"
        AllowedExtensions=".jpg,.png,.pdf"
        MaxFileSize="5242880"
        CssClass="e-upload-uploader">
    </SfUploader>
</div>
```

### Multiple File Upload

```razor
<div class="multi-upload-demo">
    <h3>Upload Multiple Files</h3>
    <SfUploader 
        AllowMultiple="true"
        AutoUpload="false"
        AllowedExtensions=".jpg,.png,.pdf"
        MaxFileSize="5242880"
        CssClass="e-upload-uploader">
    </SfUploader>
</div>
```

## Preloaded Files

### Display Preloaded Files

The FileUpload component supports displaying a list of files that are already available on the server as `UploadedFile`. This is useful for editing scenarios where users need to see and potentially remove existing files before uploading new ones.

```razor
<SfUploader>
    <UploadedFiles>
        <UploadedFile Name="Nature" Size="500000" Type=".png"></UploadedFile>
        <UploadedFile Name="TypeScript Succinctly" Size="12000" Type=".pdf"></UploadedFile>
        <UploadedFile Name="ASP.NET Webhooks" Size="500000" Type=".docx"></UploadedFile>
    </UploadedFiles>
</SfUploader>
```

## Auto Upload Configuration

### Enable Auto Upload

```razor
<SfUploader ID="UploadFiles" AutoUpload="true" AllowedExtensions=".jpg,.png,.pdf">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>
```

### Async Upload Settings

```razor
<SfUploader ID="UploadFiles" AutoUpload="true" AllowedExtensions=".jpg,.png,.pdf" MaxFileSize="10485760">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>
```

## File Validation

### Allowed Extensions

```razor
<div class="extension-validation">
    <h3>File Type Validation</h3>
    
    <div>
        <label>Images only:</label>
        <SfUploader 
            AutoUpload="false"
            AllowedExtensions=".jpg,.jpeg,.png,.gif,.bmp">
        </SfUploader>
    </div>

    <div>
        <label>Documents only:</label>
        <SfUploader 
            AutoUpload="false"
            AllowedExtensions=".pdf,.doc,.docx,.xls,.xlsx">
        </SfUploader>
    </div>

    <div>
        <label>All common formats:</label>
        <SfUploader 
            AutoUpload="false"
            AllowedExtensions=".jpg,.png,.pdf,.doc,.docx,.xls,.ppt">
        </SfUploader>
    </div>
</div>
```

### File Size Validation

```razor
<div class="size-validation">
    <h3>File Size Validation</h3>
    
    <div>
        <label>Max 5 MB:</label>
        <SfUploader 
            AutoUpload="false"
            MaxFileSize="5242880"
            AllowedExtensions=".jpg,.png,.pdf">
        </SfUploader>
    </div>

    <div>
        <label>Max 50 MB:</label>
        <SfUploader 
            AutoUpload="false"
            MaxFileSize="52428800"
            AllowedExtensions=".jpg,.png,.pdf,.mp4,.zip">
        </SfUploader>
    </div>
</div>
```

### Minimum File Size

```razor
<SfUploader 
    AutoUpload="false"
    MaxFileSize="52428800"
    MinFileSize="1024">
</SfUploader>
```

## Directory and Sequential Upload

### Directory Upload

Enable directory upload to allow uploading entire folders:

```razor
<SfUploader 
    DirectoryUpload="true"
    AutoUpload="false"
    AllowedExtensions=".jpg,.png,.pdf">
</SfUploader>
```

### Sequential Upload

Upload files one at a time in sequence when multiple files are selected:

```razor
<SfUploader 
    ID="SequentialUploader"
    SequentialUpload="true"
    AllowMultiple="true"
    AutoUpload="false"
    ShowFileList="true"
    AllowedExtensions=".jpg,.png,.pdf">
</SfUploader>
```

When `SequentialUpload` is set to `true`, files are uploaded one after another instead of simultaneously. This is useful for servers with limited bandwidth or when you want to track individual file upload progress more precisely.

## Drag and Drop

### Default Drop Effect

```razor
<div class="uploader-grid">
    <div class="uploader-card">
        <p><strong>Default:</strong></p>
        <SfUploader CssClass="e-dropeffect-uploader" ID="UploadFilesDefault" AutoUpload="false" DropEffect="DropEffect.Default" />
    </div>

    <div class="uploader-card">
        <p><strong>Copy:</strong></p>
        <SfUploader CssClass="e-dropeffect-uploader" ID="UploadFilesCopy" AutoUpload="false" DropEffect="DropEffect.Copy" />
    </div>

    <div class="uploader-card">
        <p><strong>Move:</strong></p>
        <SfUploader CssClass="e-dropeffect-uploader" ID="UploadFilesMove" AutoUpload="false" DropEffect="DropEffect.Move" />
    </div>

    <div class="uploader-card">
        <p><strong>Link:</strong></p>
        <SfUploader CssClass="e-dropeffect-uploader" ID="UploadFilesLink" AutoUpload="false" DropEffect="DropEffect.Link" />
    </div>

    <div class="uploader-card">
        <p><strong>None:</strong></p>
        <SfUploader CssClass="e-dropeffect-uploader" ID="UploadFilesNone" AutoUpload="false" DropEffect="DropEffect.None" />
    </div>
</div>
```

### Custom Drop Area

Specify a custom drop target element:

```razor
<div class="custom-dropzone" id="dropZone">
    <div class="dropzone-content">
        <p>Drag and drop files here</p>
        <SfUploader DropArea="#dropZone" AutoUpload="false" AllowedExtensions=".jpg,.png,.pdf" />
    </div>
</div>
```

## UI Customization

### Custom CSS Classes

Apply custom styling to the uploader:

```razor
<SfUploader 
    CssClass="custom-uploader"
    AutoUpload="false"
    AllowedExtensions=".jpg,.png,.pdf">
</SfUploader>
```

### Disabled State

Disable the uploader to prevent uploads:

```razor
<SfUploader 
    Disabled="true"
    AutoUpload="false"
    AllowedExtensions=".jpg,.png,.pdf">
</SfUploader>

<SfUploader 
    Disabled="true"
    AllowMultiple="true"
    AutoUpload="false"
    AllowedExtensions=".jpg,.png,.pdf">
</SfUploader>
```

### Show/Hide File List

Control whether the file list is displayed:

```razor
<div class="file-list-demo">
    <h4>With File List (default):</h4>
    <SfUploader ShowFileList="true" AutoUpload="false" AllowedExtensions=".jpg,.png,.pdf" />

    <h4>Without File List:</h4>
    <SfUploader ShowFileList="false" AutoUpload="false" AllowedExtensions=".jpg,.png,.pdf" />
</div>
```

### Show/Hide Progress Bar

Control whether the progress bar is displayed:

```razor
<div class="progress-demo">
    <h4>With Progress Bar:</h4>
    <SfUploader ShowProgressBar="true" AutoUpload="false" AllowedExtensions=".jpg,.png,.pdf" />

    <h4>Without Progress Bar:</h4>
    <SfUploader ShowProgressBar="false" AutoUpload="false" AllowedExtensions=".jpg,.png,.pdf" />
</div>
```

### State Persistence

The `EnablePersistence` property allows the uploader's state to be persisted in browser localStorage across page reloads.

```razor
<SfUploader ID="FileUploader" EnablePersistence="true" AutoUpload="false" AllowedExtensions=".jpg,.png,.pdf">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>
```

**Important:** When using `EnablePersistence`, you must also set an `ID` property on the component. The persistence mechanism uses the component's `ID` as the storage key in localStorage. Without a unique `ID`, the persistence behavior may not work correctly across multiple component instances.

## Uploader Templates

### File List Template

The File Upload component allows for the customization of the file list items by using a template:

```razor
<SfUploader ID="UploadFiles" AutoUpload="false">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save"
                           RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove"></UploaderAsyncSettings>
    <UploaderTemplates>
        <Template Context="context">
            <div style="padding: 10px;">
                <div class="name file-name" title="@context.Name">File Name : @context.Name</div>
                <div class="file-size">File Size : @context.Size</div>
                <div class="e-file-status">File Status : @context.Status</div>
            </div>
        </Template>
    </UploaderTemplates>
</SfUploader>
```

## Upload Events

### OnValueChange Event

Fired when files are selected:

```razor
<SfUploader
    OnValueChange="@OnFilesChanged"
    AllowedExtensions=".jpg,.png,.pdf">
</SfUploader>

@code {
    private void OnFilesChanged(UploadChangeEventArgs args)
    {
        Console.WriteLine($"Files changed:");
        foreach (var file in args.Files)
        {
            Console.WriteLine($"  - {file.FileInfo.Name} ({file.FileInfo.Size} bytes)");
        }
    }
}
```

### Uploading Event

Fired before uploading starts:

```razor
<SfUploader ID="UploadFiles" OnUploadStart="@OnUploading" AutoUpload="false">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>

@code {
    private void OnUploading(UploadingEventArgs args)
    {
        Console.WriteLine($"Uploading {args.FileData.Name}");
    }
}
```

### Success Event

Fired when upload completes successfully:

```razor
<SfUploader ID="UploadFiles" Success="@OnUploadSuccess" AutoUpload="true">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>

@code {
    private void OnUploadSuccess(SuccessEventArgs args)
    {
        Console.WriteLine($"Upload successful: {args.File.Name}");
    }
}
```

### Failure Event

Fired when upload fails:

```razor
<SfUploader OnFailure="@OnUploadFailure" AutoUpload="true">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>

<p class="error-message">@errorMessage</p>

@code {
    private string errorMessage = "";

    private void OnUploadFailure(FailureEventArgs args)
    {
        errorMessage = $"Upload failed: {args.File.Name}";
        Console.WriteLine(errorMessage);
    }
}
```

## Progress and Cancellation

### Progress Tracking

```razor
<div class="progress-demo">
    <SfUploader @ref="uploader" ID="UploadFiles" Progressing="@OnUploadProgress" AutoUpload="false">
        <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
    </SfUploader>

    <div class="progress-info">
        <p>Uploaded: @uploadedPercent%</p>
        <progress value="@uploadedPercent" max="100"></progress>
    </div>
</div>

@code {
    private SfUploader uploader = new();
    private int uploadedPercent = 0;

    private void OnUploadProgress(ProgressEventArgs args)
    {
        if (args.Total > 0)
        {
            uploadedPercent = (int)((args.Loaded / args.Total) * 100);
        }
        Console.WriteLine($"Progress: {uploadedPercent}%");
    }
}
```

### Pause and Resume

```razor
<div class="pause-resume-demo">
    <SfUploader @ref="uploader" ID="UploadFiles" Paused="@OnPaused" OnResume="@OnResumed" AutoUpload="false">
        <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" ChunkSize="500000" />
    </SfUploader>

    <button @onclick="PauseUpload">Pause</button>
    <button @onclick="ResumeUpload">Resume</button>
    <button @onclick="CancelUpload">Cancel</button>
</div>

@code {
    private SfUploader uploader = new();

    private void OnPaused(PauseResumeEventArgs args)
    {
        Console.WriteLine($"Upload paused: {args.File.Name}");
    }

    private void OnResumed(PauseResumeEventArgs args)
    {
        Console.WriteLine($"Upload resumed: {args.File.Name}");
    }

    private async Task PauseUpload()
    {
        await uploader.PauseAsync();
    }

    private async Task ResumeUpload()
    {
        await uploader.ResumeAsync();
    }

    private async Task CancelUpload()
    {
        await uploader.CancelAsync();
    }
}
```

### OnChunkUploadStart Event

Fired when each chunk upload starts (requires ChunkSize to be set):

```razor
<SfUploader ID="UploadFiles" OnChunkUploadStart="@OnChunkStart" AutoUpload="false">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" ChunkSize="500000" />
</SfUploader>

@code {
    private void OnChunkStart(UploadingEventArgs args)
    {
        Console.WriteLine($"Chunk upload started for: {args.FileData.Name}");
        // Pass custom data with chunk
        // args.CustomFormData = new List<object> { new { Authorization = "token" } };
    }
}
```

### OnChunkSuccess Event

Fired when each chunk uploads successfully:

```razor
<SfUploader ID="UploadFiles" OnChunkSuccess="@OnChunkSuccess" AutoUpload="false">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" ChunkSize="500000" />
</SfUploader>

@code {
    private void OnChunkSuccess(SuccessEventArgs args)
    {
        Console.WriteLine($"Chunk uploaded successfully: {args.File.Name}");
    }
}
```

### OnChunkFailure Event

Fired when a chunk upload fails:

```razor
<SfUploader ID="UploadFiles" OnChunkFailure="@OnChunkFailure" AutoUpload="false">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" ChunkSize="500000" />
</SfUploader>

@code {
    private void OnChunkFailure(FailureEventArgs args)
    {
        Console.WriteLine($"Chunk upload failed: {args.File.Name}");
    }
}
```

### OnActionComplete Event

Fired when all upload/remove operations complete:

```razor
<SfUploader ID="UploadFiles" OnActionComplete="@OnComplete" AutoUpload="true">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>

@code {
    private void OnComplete(ActionCompleteEventArgs args)
    {
        foreach (var file in args.FileData)
        {
            Console.WriteLine($"File completed: {file.Name} - Status: {file.Status}");
        }
    }
}
```

### Created Event

Fired when the uploader component is created:

```razor
<SfUploader ID="UploadFiles" Created="@OnCreated" AutoUpload="false">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>

@code {
    private void OnCreated(object args)
    {
        Console.WriteLine("Uploader created");
    }
}
```

### OnClear Event

Fired before clearing all files from the list:

```razor
<SfUploader ID="UploadFiles" OnClear="@OnClear" AutoUpload="false">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>

@code {
    private void OnClear(ClearingEventArgs args)
    {
        Console.WriteLine($"Clearing {args.FilesData.Count} files");
    }
}
```

## Error Handling

### Validation Error Handling

Use the `FileSelected` event to validate files when they are selected. Set `args.Cancel = true` to reject invalid files:

```razor
<div class="error-handling-demo">
    <SfUploader 
        AutoUpload="false"
        AllowedExtensions=".jpg,.png,.pdf"
        MaxFileSize="5242880"
        FileSelected="@OnFileSelected">
    </SfUploader>

    @if (!string.IsNullOrEmpty(validationError))
    {
        <div class="error-message" style="color: #dc3545; margin-top: 10px;">@validationError</div>
    }
</div>

@code {
    private string validationError = "";

    private void OnFileSelected(SelectedEventArgs args)
    {
        validationError = "";

        foreach (var file in args.FilesData)
        {
            // Check file size
            if (file.Size > 5242880)
            {
                validationError = $"File {file.Name} exceeds 5MB limit";
                args.Cancel = true;
                return;
            }

            // Check extension
            var validExtensions = new[] { ".jpg", ".png", ".pdf" };
            var extension = System.IO.Path.GetExtension(file.Name);
            if (!validExtensions.Contains(extension.ToLower()))
            {
                validationError = $"File type {extension} not allowed";
                args.Cancel = true;
                return;
            }
        }
    }
}
```

### Server-Side Error Handling

```razor
<SfUploader OnFailure="@OnUploadFailure" AutoUpload="true">
    <UploaderAsyncSettings SaveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/save" RemoveUrl="https://blazor.syncfusion.com/services/production/api/FileUploader/remove" />
</SfUploader>

<div class="upload-status">
    <p>Status: @statusMessage</p>
    <p class="error">@errorMessage</p>
</div>

@code {
    private string statusMessage = "";
    private string errorMessage = "";

    private void OnUploadFailure(FailureEventArgs args)
    {
        statusMessage = "Upload failed";
        errorMessage = $"Error uploading {args.File?.Name}: {args.StatusText}";
        Console.WriteLine(errorMessage);
    }
}
```

## Practical Examples

### Profile Picture Upload

```razor
<div class="profile-upload">
    <h2>Upload Profile Picture</h2>

    <div class="current-picture">
        @if (!string.IsNullOrEmpty(profilePictureUrl))
        {
            <img src="@profilePictureUrl" alt="Profile" />
        }
        else
        {
            <p>No image uploaded</p>
        }
    </div>

    <SfUploader ID="UploadFiles" AutoUpload="true" AllowedExtensions=".jpg,.jpeg,.png,.gif" MaxFileSize="2097152" Success="@OnProfilePictureUploaded">
        <UploaderAsyncSettings SaveUrl="/api/profile/upload-picture" RemoveUrl="/api/profile/remove-picture" />
    </SfUploader>
</div>

@code {
    private string profilePictureUrl = "";

    private void OnProfilePictureUploaded(SuccessEventArgs args)
    {
        profilePictureUrl = $"/uploads/profile/{args.File.Name}";
        Console.WriteLine($"Profile picture uploaded: {profilePictureUrl}");
    }
}
```

### Bulk Image Upload with Preview

```razor
<div class="image-gallery-upload">
    <h2>Bulk Image Upload</h2>

    <SfUploader 
        ID="UploadFiles"
        AllowMultiple="true"
        AutoUpload="true"
        AllowedExtensions=".jpg,.jpeg,.png,.gif"
        MaxFileSize="5242880"
        Success="@OnImageUploaded">
        <UploaderAsyncSettings SaveUrl="/api/images/upload" RemoveUrl="/api/images/delete" />
    </SfUploader>

    <div class="image-gallery">
        <h3>Uploaded Images:</h3>
        <div class="gallery-grid">
            @foreach (var image in uploadedImages)
            {
                <div class="gallery-item">
                    <img src="@image.Url" alt="@image.Name" />
                    <p>@image.Name</p>
                </div>
            }
        </div>
    </div>
</div>

@code {
    private List<ImageInfo> uploadedImages = new();

    private void OnImageUploaded(SuccessEventArgs args)
    {
        uploadedImages.Add(new ImageInfo 
        { 
            Name = args.File.Name,
            Url = $"/uploads/images/{args.File.Name}"
        });
    }

    public class ImageInfo
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }
}
```

### Form with File Upload

```razor
<div class="support-form">
    <h2>Submit Support Ticket</h2>

    <EditForm Model="@supportModel" OnValidSubmit="@HandleSubmit">
        <div class="form-group">
            <label>Issue Title:</label>
            <SfTextBox @bind-Value="@supportModel.Title" Placeholder="Brief description of your issue" />
        </div>

        <div class="form-group">
            <label>Description:</label>
            <SfTextArea @bind-Value="@supportModel.Description" Placeholder="Detailed description" RowCount="4" />
        </div>

        <div class="form-group">
            <label>Attachments:</label>
            <SfUploader
                ID="UploadFiles"
                AllowMultiple="true"
                AutoUpload="true"
                ShowFileList="true"
                ShowProgressBar="true"
                AllowedExtensions=".jpg,.png,.pdf,.zip"
                MaxFileSize="10485760">
                <UploaderAsyncSettings SaveUrl="/api/support/upload" RemoveUrl="/api/support/remove" />
            </SfUploader>
        </div>

        <button type="submit">Submit Ticket</button>
    </EditForm>
</div>

@code {
    private SupportModel supportModel = new();

    private void HandleSubmit()
    {
        Console.WriteLine($"Support ticket: {supportModel.Title}");
    }

    private class SupportModel
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
```
