using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Logging;
using ImGuiNET;
using OtterGui;
using OtterGui.Classes;
using OtterGui.Raii;
using Penumbra.GameData.ByteString;
using Penumbra.Mods;
using Penumbra.Util;

namespace Penumbra.UI.Classes;

public partial class ModEditWindow
{
    private readonly HashSet< Mod.Editor.FileRegistry > _selectedFiles = new(256);
    private          LowerString                        _fileFilter    = LowerString.Empty;
    private          bool                               _showGamePaths = true;
    private          string                             _gamePathEdit  = string.Empty;
    private          int                                _fileIdx       = -1;
    private          int                                _pathIdx       = -1;
    private          int                                _folderSkip    = 0;

    private bool CheckFilter( Mod.Editor.FileRegistry registry )
        => _fileFilter.IsEmpty || registry.File.FullName.Contains( _fileFilter.Lower, StringComparison.OrdinalIgnoreCase );

    private bool CheckFilter( (Mod.Editor.FileRegistry, int) p )
        => CheckFilter( p.Item1 );

    private void DrawFileTab()
    {
        using var tab = ImRaii.TabItem( "File Redirections" );
        if( !tab )
        {
            return;
        }

        DrawOptionSelectHeader();
        DrawButtonHeader();

        using var child = ImRaii.Child( "##files", -Vector2.One, true );
        if( !child )
        {
            return;
        }

        using var list = ImRaii.Table( "##table", 1 );
        if( !list )
        {
            return;
        }

        foreach( var (registry, i) in _editor!.AvailableFiles.WithIndex().Where( CheckFilter ) )
        {
            using var id = ImRaii.PushId( i );
            ImGui.TableNextColumn();

            DrawSelectable( registry );

            if( !_showGamePaths )
            {
                continue;
            }

            using var indent = ImRaii.PushIndent( 50f );
            for( var j = 0; j < registry.SubModUsage.Count; ++j )
            {
                var (subMod, gamePath) = registry.SubModUsage[ j ];
                if( subMod != _editor.CurrentOption )
                {
                    continue;
                }

                PrintGamePath( i, j, registry, subMod, gamePath );
            }

            PrintNewGamePath( i, registry, _editor.CurrentOption );
        }
    }

    private string DrawFileTooltip( Mod.Editor.FileRegistry registry, ColorId color )
    {
        (string, int) GetMulti()
        {
            var groups = registry.SubModUsage.GroupBy( s => s.Item1 ).ToArray();
            return ( string.Join( "\n", groups.Select( g => g.Key.Name ) ), groups.Length );
        }

        var (text, groupCount) = color switch
        {
            ColorId.ConflictingMod => ( string.Empty, 0 ),
            ColorId.NewMod         => ( registry.SubModUsage[ 0 ].Item1.Name, 1 ),
            ColorId.InheritedMod   => GetMulti(),
            _                      => ( string.Empty, 0 ),
        };

        if( text.Length > 0 && ImGui.IsItemHovered() )
        {
            ImGui.SetTooltip( text );
        }


        return ( groupCount, registry.SubModUsage.Count ) switch
        {
            (0, 0)   => "(unused)",
            (1, 1)   => "(used 1 time)",
            (1, > 1) => $"(used {registry.SubModUsage.Count} times in 1 group)",
            _        => $"(used {registry.SubModUsage.Count} times over {groupCount} groups)",
        };
    }

    private void DrawSelectable( Mod.Editor.FileRegistry registry )
    {
        var selected = _selectedFiles.Contains( registry );
        var color = registry.SubModUsage.Count == 0                          ? ColorId.ConflictingMod :
            registry.CurrentUsage              == registry.SubModUsage.Count ? ColorId.NewMod : ColorId.InheritedMod;
        using var c = ImRaii.PushColor( ImGuiCol.Text, color.Value() );
        if( ConfigWindow.Selectable( registry.RelPath.Path, selected ) )
        {
            if( selected )
            {
                _selectedFiles.Remove( registry );
            }
            else
            {
                _selectedFiles.Add( registry );
            }
        }

        var rightText = DrawFileTooltip( registry, color );

        ImGui.SameLine();
        ImGuiUtil.RightAlign( rightText );
    }

    private void PrintGamePath( int i, int j, Mod.Editor.FileRegistry registry, ISubMod subMod, Utf8GamePath gamePath )
    {
        using var id = ImRaii.PushId( j );
        ImGui.TableNextColumn();
        var tmp = _fileIdx == i && _pathIdx == j ? _gamePathEdit : gamePath.ToString();

        ImGui.SetNextItemWidth( -1 );
        if( ImGui.InputText( string.Empty, ref tmp, Utf8GamePath.MaxGamePathLength ) )
        {
            _fileIdx      = i;
            _pathIdx      = j;
            _gamePathEdit = tmp;
        }

        ImGuiUtil.HoverTooltip( "Clear completely to remove the path from this mod." );

        if( ImGui.IsItemDeactivatedAfterEdit() )
        {
            if( Utf8GamePath.FromString( _gamePathEdit, out var path, false ) )
            {
                _editor!.SetGamePath( _fileIdx, _pathIdx, path );
            }
            _fileIdx = -1;
            _pathIdx = -1;
        }
    }

    private void PrintNewGamePath( int i, Mod.Editor.FileRegistry registry, ISubMod subMod )
    {
        var tmp = _fileIdx == i && _pathIdx == -1 ? _gamePathEdit : string.Empty;
        ImGui.SetNextItemWidth( -1 );
        if( ImGui.InputTextWithHint( "##new", "Add New Path...", ref tmp, Utf8GamePath.MaxGamePathLength ) )
        {
            _fileIdx      = i;
            _pathIdx      = -1;
            _gamePathEdit = tmp;
        }

        if( ImGui.IsItemDeactivatedAfterEdit() )
        {
            if( Utf8GamePath.FromString( _gamePathEdit, out var path, false ) && !path.IsEmpty )
            {
                _editor!.SetGamePath( _fileIdx, _pathIdx, path );
            }
            _fileIdx = -1;
            _pathIdx = -1;
        }
    }

    private void DrawButtonHeader()
    {
        ImGui.NewLine();

        using var spacing = ImRaii.PushStyle( ImGuiStyleVar.ItemSpacing, new Vector2( 3 * ImGuiHelpers.GlobalScale, 0 ) );
        ImGui.SetNextItemWidth( 30 * ImGuiHelpers.GlobalScale );
        ImGui.DragInt( "##skippedFolders", ref _folderSkip, 0.01f, 0, 10 );
        ImGuiUtil.HoverTooltip( "Skip the first N folders when automatically constructing the game path from the file path." );
        ImGui.SameLine();
        spacing.Pop();
        if( ImGui.Button( "Add Paths" ) )
        {
            _editor!.AddPathsToSelected( _editor!.AvailableFiles.Where( _selectedFiles.Contains ), _folderSkip );
        }

        ImGuiUtil.HoverTooltip(
            "Add the file path converted to a game path to all selected files for the current option, optionally skipping the first N folders." );


        ImGui.SameLine();
        if( ImGui.Button( "Remove Paths" ) )
        {
            _editor!.RemovePathsFromSelected( _editor!.AvailableFiles.Where( _selectedFiles.Contains ) );
        }

        ImGuiUtil.HoverTooltip( "Remove all game paths associated with the selected files in the current option." );


        ImGui.SameLine();
        if( ImGui.Button( "Delete Selected Files" ) )
        {
            _editor!.DeleteFiles( _editor!.AvailableFiles.Where( _selectedFiles.Contains ) );
        }

        ImGuiUtil.HoverTooltip(
            "Delete all selected files entirely from your filesystem, but not their file associations in the mod, if there are any.\n!!!This can not be reverted!!!" );
        ImGui.SameLine();
        var changes = _editor!.FileChanges;
        var tt      = changes ? "Apply the current file setup to the currently selected option." : "No changes made.";
        if( ImGuiUtil.DrawDisabledButton( "Apply Changes", Vector2.Zero, tt, !changes ) )
        {
            var failedFiles = _editor!.ApplyFiles();
            PluginLog.Information( $"Failed to apply {failedFiles} file redirections to {_editor.CurrentOption.Name}." );
        }


        ImGui.SameLine();
        var label  = changes ? "Revert Changes" : "Reload Files";
        var length = new Vector2( ImGui.CalcTextSize( "Revert Changes" ).X, 0 );
        if( ImGui.Button( label, length ) )
        {
            _editor!.RevertFiles();
        }

        ImGuiUtil.HoverTooltip( "Revert all revertible changes since the last file or option reload or data refresh." );

        ImGui.SetNextItemWidth( 250 * ImGuiHelpers.GlobalScale );
        LowerString.InputWithHint( "##filter", "Filter paths...", ref _fileFilter, Utf8GamePath.MaxGamePathLength );
        ImGui.SameLine();
        ImGui.Checkbox( "Show Game Paths", ref _showGamePaths );
        ImGui.SameLine();
        if( ImGui.Button( "Unselect All" ) )
        {
            _selectedFiles.Clear();
        }

        ImGui.SameLine();
        if( ImGui.Button( "Select Visible" ) )
        {
            _selectedFiles.UnionWith( _editor!.AvailableFiles.Where( CheckFilter ) );
        }

        ImGui.SameLine();
        if( ImGui.Button( "Select Unused" ) )
        {
            _selectedFiles.UnionWith( _editor!.AvailableFiles.Where( f => f.SubModUsage.Count == 0 ) );
        }

        ImGui.SameLine();
        if( ImGui.Button( "Select Used Here" ) )
        {
            _selectedFiles.UnionWith( _editor!.AvailableFiles.Where( f => f.CurrentUsage > 0 ) );
        }

        ImGui.SameLine();

        ImGuiUtil.RightAlign( $"{_selectedFiles.Count} / {_editor!.AvailableFiles.Count} Files Selected" );
    }
}