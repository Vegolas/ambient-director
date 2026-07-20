// Trigger a browser file download from a .NET stream (Blazor's DotNetStreamReference). The C# side
// fetches the bytes with the API key attached, then hands the response stream here so the key never
// has to ride in a URL. Used by the Settings → Backup "download database" button (issue #110).
window.downloadFileFromStream = async (fileName, contentStreamReference) => {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer]);
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName ?? 'download';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
};
