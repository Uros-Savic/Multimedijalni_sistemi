using MSI.Client.Services;

namespace MSI.Client;

public sealed partial class MainForm
{
    private async void BtnBatch_Click(object? s, EventArgs e)
    {
        if (_currentImage == null || string.IsNullOrEmpty(_sessionId))
        { ShowErr("Najpre ucitajte sliku."); return; }

        using var dlg = new BatchFilterDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.SelectedSteps.Count == 0) return;

        string chain = string.Join(" - ", dlg.SelectedSteps.Select(st => st.Name));
        SetStatus($"Batch: {chain}...");
        SetBusy(true);
        try
        {
            _history.Push(_currentImage, _currentLabel, _sessionId);
            var req = new FilterRequest(_sessionId, dlg.SelectedSteps, "png");
            var resp = await _api.ApplyFiltersAsync(req);
            var parts = resp.DownloadUrl.Split('/');
            byte[] full = await _api.DownloadAsync(_sessionId, parts[^2], "png");
            _currentImage?.Dispose();
            using var ms = new MemoryStream(full);
            _currentImage = new Bitmap(ms);
            _currentLabel = chain;
            lblInfo.Text = $"BATCH: {dlg.SelectedSteps.Count} filtera  {resp.ProcessingMs}ms";
            lblInfo.Visible = true;
            pnlCanvas.Invalidate();
            UpdateButtonStates();
            SetStatus($"Batch zavrsen: {chain} | {resp.ProcessingMs}ms");
            _log.Info($"Batch OK: [{chain}] | {resp.ProcessingMs}ms");
        }
        catch (Exception ex)
        {
            _log.Error("Batch greska", ex);
            ShowErr($"Greska pri batch primeni:\n{ex.Message}");
            SetStatus("Batch neuspesan.");
        }
        finally { SetBusy(false); }
    }
}
