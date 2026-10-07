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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace QuickLook.Plugin.PrprojViewer;

public enum MediaStatus { Online, Moved, Offline }

public sealed class SequenceInfo
{
    public string Name { get; set; }
    public long DurationTicks { get; set; }
    public double Fps { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int VideoTracks { get; set; }
    public int AudioTracks { get; set; }

    public string Duration => DurationTicks <= 0 ? "empty" : PrprojReader.Timecode(DurationTicks, Fps);
    public string Resolution => Width > 0 ? $"{Width} × {Height}" : "—";
    public string FrameRate => Fps > 0 ? PrprojReader.FormatFps(Fps) : "—";
    public string Tracks => $"V{VideoTracks}  A{AudioTracks}";
}

public sealed class MediaInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string FoundAt { get; set; }
    public MediaStatus Status { get; set; }

    public string Ext => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public string Folder => System.IO.Path.GetDirectoryName(Status == MediaStatus.Moved ? FoundAt : Path);
    public string Note => Status switch
    {
        MediaStatus.Offline => "Offline",
        MediaStatus.Moved => "Moved",
        _ => "",
    };
    public string ToolTipText => Status == MediaStatus.Moved ? $"Found at: {FoundAt}\nSaved path: {Path}" : Path;
}

public sealed class ProjectInfo
{
    public List<SequenceInfo> Sequences { get; } = new();
    public List<MediaInfo> Media { get; } = new();
    public int OfflineCount => Media.Count(m => m.Status == MediaStatus.Offline);
    public int MovedCount => Media.Count(m => m.Status == MediaStatus.Moved);
}

public static class PrprojReader
{
    public const long TicksPerSecond = 254016000000;

    private static readonly HashSet<string> Wanted = new()
    {
        "Sequence", "Media", "VideoTrackGroup", "AudioTrackGroup",
        "VideoClipTrack", "AudioClipTrack", "VideoClipTrackItem", "AudioClipTrackItem",
    };

    public static ProjectInfo Read(string path)
    {
        var objects = new Dictionary<string, XElement>();
        var sequences = new List<XElement>();
        var media = new List<XElement>();

        // read-only and shared, so it works while Premiere has the project open
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var gz = new GZipStream(fs, CompressionMode.Decompress))
        using (var xr = XmlReader.Create(gz, new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore }))
        {
            xr.MoveToContent();
            xr.Read();
            // only keep the top-level objects we need; big projects are hundreds of MB of XML
            while (!xr.EOF)
            {
                if (xr.NodeType != XmlNodeType.Element || xr.Depth != 1 || !Wanted.Contains(xr.LocalName))
                {
                    if (xr.NodeType == XmlNodeType.Element && xr.Depth == 1) xr.Skip();
                    else xr.Read();
                    continue;
                }

                var e = (XElement)XNode.ReadFrom(xr);
                var id = (string)e.Attribute("ObjectID") ?? (string)e.Attribute("ObjectUID");
                if (id != null) objects[id] = e;
                if (e.Name == "Sequence") sequences.Add(e);
                else if (e.Name == "Media") media.Add(e);
            }
        }

        var info = new ProjectInfo();
        foreach (var s in sequences) info.Sequences.Add(ReadSequence(s, objects));
        info.Sequences.Sort((a, b) => StrCmpLogical(a.Name, b.Name));

        ReadMedia(media, Path.GetDirectoryName(path), info.Media);
        return info;
    }

    private static SequenceInfo ReadSequence(XElement seq, Dictionary<string, XElement> objects)
    {
        var info = new SequenceInfo { Name = (string)seq.Element("Name") ?? "(unnamed)" };

        foreach (var tg in seq.Element("TrackGroups")?.Elements("TrackGroup") ?? Enumerable.Empty<XElement>())
        {
            var group = Resolve(tg.Element("Second"), objects);
            if (group == null) continue;

            var tracks = group.Element("TrackGroup")?.Element("Tracks")?.Elements("Track").ToList() ?? new List<XElement>();
            if (group.Name == "VideoTrackGroup")
            {
                var frame = (long?)group.Element("TrackGroup")?.Element("FrameRate") ?? 0;
                if (frame > 0) info.Fps = (double)TicksPerSecond / frame;
                var rect = ((string)group.Element("FrameRect"))?.Split(',');
                if (rect?.Length == 4)
                {
                    info.Width = int.Parse(rect[2], CultureInfo.InvariantCulture);
                    info.Height = int.Parse(rect[3], CultureInfo.InvariantCulture);
                }
                info.VideoTracks = tracks.Count;
            }
            else if (group.Name == "AudioTrackGroup")
            {
                info.AudioTracks = tracks.Count;
            }
            else continue;

            foreach (var t in tracks)
            {
                var track = Resolve(t, objects);
                var items = track?.Element("ClipTrack")?.Element("ClipItems")?.Element("TrackItems")?.Elements("TrackItem");
                if (items == null) continue;
                foreach (var ti in items)
                {
                    var end = (long?)Resolve(ti, objects)?.Element("ClipTrackItem")?.Element("TrackItem")?.Element("End") ?? 0;
                    if (end > info.DurationTicks) info.DurationTicks = end;
                }
            }
        }
        return info;
    }

    private static void ReadMedia(List<XElement> media, string projectDir, List<MediaInfo> result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var drives = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in media)
        {
            var path = (string)m.Element("ActualMediaFilePath") ?? (string)m.Element("FilePath");
            // synthetic items (black video, bars, transcripts...) carry an ID instead of a path
            if (string.IsNullOrEmpty(path) || !LooksLikePath(path) || !seen.Add(path)) continue;

            var item = new MediaInfo
            {
                Path = path,
                Name = (string)m.Element("Title") ?? System.IO.Path.GetFileName(path),
                Status = MediaStatus.Offline,
            };

            if (Exists(path, drives))
            {
                item.Status = MediaStatus.Online;
            }
            else
            {
                // Premiere relinks on its own through RelativePath when the whole folder was moved
                var rel = (string)m.Element("RelativePath");
                if (!string.IsNullOrEmpty(rel) && projectDir != null)
                {
                    try
                    {
                        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, rel));
                        if (Exists(full, drives))
                        {
                            item.Status = MediaStatus.Moved;
                            item.FoundAt = full;
                        }
                    }
                    catch { }
                }
            }
            result.Add(item);
        }

        result.Sort((a, b) => a.Status != b.Status
            ? b.Status.CompareTo(a.Status)
            : StrCmpLogical(a.Name, b.Name));
    }

    private static bool LooksLikePath(string p) =>
        p.Length > 2 && (p[1] == ':' || p.StartsWith(@"\\") || p.StartsWith("/"));

    private static bool Exists(string path, Dictionary<string, bool> drives)
    {
        try
        {
            // a missing drive or share would make every File.Exists on it hang for a while
            var root = System.IO.Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;
            if (!drives.TryGetValue(root, out var ok))
                drives[root] = ok = Directory.Exists(root);
            return ok && File.Exists(path);
        }
        catch { return false; }
    }

    private static XElement Resolve(XElement reference, Dictionary<string, XElement> objects)
    {
        if (reference == null) return null;
        var id = (string)reference.Attribute("ObjectRef") ?? (string)reference.Attribute("ObjectURef");
        return id != null && objects.TryGetValue(id, out var e) ? e : null;
    }

    public static string Timecode(long ticks, double fps)
    {
        var nominal = fps > 0 ? (int)Math.Round(fps) : 30;
        var totalFrames = (long)Math.Round(ticks / (double)TicksPerSecond * (fps > 0 ? fps : 30));
        var ff = totalFrames % nominal;
        var totalSeconds = totalFrames / nominal;
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}:{3:00}",
            totalSeconds / 3600, totalSeconds / 60 % 60, totalSeconds % 60, ff);
    }

    public static string FormatFps(double fps) =>
        Math.Round(fps, 3).ToString("0.###", CultureInfo.InvariantCulture) + " fps";

    private static int StrCmpLogical(string a, string b) => NativeMethods.StrCmpLogicalW(a ?? "", b ?? "");
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int StrCmpLogicalW(string a, string b);
}
