namespace RpgSceneMaker.Ui.Services;

/// <summary>Shared UI state: toast messages and API connectivity, rendered by the layout.</summary>
public class UiState
{
    private int _toastVersion;

    public string? Toast { get; private set; }
    public bool ToastIsError { get; private set; }
    public bool Connected { get; private set; } = true;

    public event Action? Changed;

    public void ReportOk(string message) => ShowToast(message, isError: false);

    public void ReportError(string message) => ShowToast(message, isError: true);

    public void SetConnected(bool connected)
    {
        if (Connected == connected) return;
        Connected = connected;
        Changed?.Invoke();
    }

    private async void ShowToast(string message, bool isError)
    {
        var version = ++_toastVersion;
        Toast = message;
        ToastIsError = isError;
        Changed?.Invoke();

        await Task.Delay(isError ? 5000 : 2500);
        if (version != _toastVersion) return; // a newer toast replaced this one
        Toast = null;
        Changed?.Invoke();
    }
}
