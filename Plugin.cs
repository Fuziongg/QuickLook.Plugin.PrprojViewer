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

using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace QuickLook.Plugin.PrprojViewer;

public sealed class Plugin : IViewer
{
    private ProjectView _view;

    public int Priority => 0;

    public void Init()
    {
    }

    public bool CanHandle(string path) =>
        !Directory.Exists(path) && path.EndsWith(".prproj", StringComparison.OrdinalIgnoreCase);

    public void Prepare(string path, ContextObject context)
    {
        context.PreferredSize = new Size(960, 640);
        context.Theme = OSThemeHelper.AppsUseDarkTheme() ? Themes.Dark : Themes.Light;
    }

    public void View(string path, ContextObject context)
    {
        _view = new ProjectView();
        context.ViewerContent = _view;
        context.Title = Path.GetFileName(path);
        context.IsBusy = true;

        var view = _view;
        Task.Run(() =>
        {
            ProjectInfo info = null;
            string error = null;
            try { info = PrprojReader.Read(path); }
            catch (Exception ex) { error = "Couldn't read this project.\n" + ex.Message; }

            view.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                // the user may already be previewing another file
                if (_view != view) return;
                if (info != null) view.Show(info);
                else view.ShowError(error);
                context.IsBusy = false;
            }));
        });
    }

    public void Cleanup()
    {
        _view = null;
    }
}
