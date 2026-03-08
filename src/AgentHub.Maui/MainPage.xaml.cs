using AgentHub.Contracts;

namespace AgentHub.Maui;

public partial class MainPage : ContentPage
{
    private readonly ApiClient _api;
    private CancellationTokenSource? _sseCts;

    public MainPage(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        _ = RefreshAsync();
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        var sessions = await _api.ListSessionsAsync();
        SessionsList.ItemsSource = sessions;
    }

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SessionSummary s) return;

        _sseCts?.Cancel();
        _sseCts = new CancellationTokenSource();

        LogBox.Text = "";

        await foreach (var ev in _api.StreamEventsAsync(s.SessionId, _sseCts.Token))
        {
            LogBox.Text += $"[{ev.TsUtc:HH:mm:ss}] {ev.Kind}: {ev.Data}\n";
        }
    }
}
