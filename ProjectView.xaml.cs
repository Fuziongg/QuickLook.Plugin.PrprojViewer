// Copyright © 2026 Fuziongg
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickLook.Plugin.PrprojViewer;

public partial class ProjectView : UserControl
{
    private ProjectInfo _info;

    public ProjectView()
    {
        InitializeComponent();
        TabSequences.Content = "Sequences";
        TabMedia.Content = "Media";
    }

    public void Show(ProjectInfo info)
    {
        _info = info;

        TabSequences.Content = $"Sequences  {info.Sequences.Count}";
        TabMedia.Content = $"Media  {info.Media.Count}";

        Summary.Children.Clear();
        AddChip($"{info.Sequences.Count} {Plural(info.Sequences.Count, "sequence")}", null, null);
        AddChip($"{info.Media.Count} {Plural(info.Media.Count, "media file")}", null, null);
        if (info.OfflineCount > 0)
            AddChip($"{info.OfflineCount} offline", (Brush)FindResource("OfflineBrush"), "Not found at the saved path nor next to the project.");
        else if (info.Media.Count > 0)
            AddChip("all media found", null, null);
        if (info.MovedCount > 0)
            AddChip($"{info.MovedCount} moved", (Brush)FindResource("MovedBrush"),
                "Not at the saved path, but found relative to the project file.\nPremiere usually relinks these on its own.");

        OfflineOnly.Visibility = info.OfflineCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        SequenceList.ItemsSource = info.Sequences;
        ApplyMediaFilter();
        UpdateTab();
    }

    public void ShowError(string text)
    {
        Summary.Children.Clear();
        SequencesPanel.Visibility = Visibility.Collapsed;
        MediaList.Visibility = Visibility.Collapsed;
        Message.Text = text;
        Message.Visibility = Visibility.Visible;
    }

    private void AddChip(string text, Brush accent, string tip)
    {
        var tb = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold };
        var chip = new Border
        {
            Child = tb,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(0, 0, 6, 4),
            ToolTip = tip,
        };
        if (accent != null)
        {
            chip.Background = accent;
            tb.Foreground = Brushes.White;
        }
        else chip.Background = (Brush)FindResource("Surface");
        Summary.Children.Add(chip);
    }

    private static string Plural(int n, string word) => n == 1 ? word : word + "s";

    private void Tab_Checked(object sender, RoutedEventArgs e) => UpdateTab();

    private void UpdateTab()
    {
        if (SequencesPanel == null || _info == null) return;
        var media = TabMedia.IsChecked == true;
        SequencesPanel.Visibility = media ? Visibility.Collapsed : Visibility.Visible;
        MediaList.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        OfflineOnly.Visibility = media && _info.OfflineCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        var empty = media ? _info.Media.Count == 0 : _info.Sequences.Count == 0;
        Message.Text = media ? "No media in this project." : "No sequences in this project.";
        Message.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OfflineOnly_Changed(object sender, RoutedEventArgs e) => ApplyMediaFilter();

    private void ApplyMediaFilter()
    {
        if (_info == null) return;
        MediaList.ItemsSource = OfflineOnly.IsChecked == true
            ? _info.Media.Where(m => m.Status == MediaStatus.Offline).ToList()
            : _info.Media;
    }

    private void Media_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not MediaInfo m) return;
        var target = m.Status == MediaStatus.Moved ? m.FoundAt : m.Path;
        if (m.Status == MediaStatus.Offline || !File.Exists(target)) return;
        Process.Start("explorer.exe", $"/select,\"{target}\"");
    }
}
