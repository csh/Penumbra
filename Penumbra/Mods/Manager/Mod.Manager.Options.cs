using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Logging;
using OtterGui.Filesystem;
using Penumbra.GameData.ByteString;
using Penumbra.Meta.Manipulations;
using Penumbra.Util;

namespace Penumbra.Mods;

public enum ModOptionChangeType
{
    GroupRenamed,
    GroupAdded,
    GroupDeleted,
    GroupMoved,
    GroupTypeChanged,
    PriorityChanged,
    OptionAdded,
    OptionDeleted,
    OptionMoved,
    OptionFilesChanged,
    OptionSwapsChanged,
    OptionMetaChanged,
    OptionUpdated,
    DisplayChange,
}

public sealed partial class Mod
{
    public sealed partial class Manager
    {
        public delegate void ModOptionChangeDelegate( ModOptionChangeType type, Mod mod, int groupIdx, int optionIdx, int movedToIdx );
        public event ModOptionChangeDelegate ModOptionChanged;

        public void ChangeModGroupType( Mod mod, int groupIdx, SelectType type )
        {
            var group = mod._groups[ groupIdx ];
            if( group.Type == type )
            {
                return;
            }

            mod._groups[ groupIdx ] = group.Convert( type );
            ModOptionChanged.Invoke( ModOptionChangeType.GroupTypeChanged, mod, groupIdx, -1, -1 );
        }

        public void RenameModGroup( Mod mod, int groupIdx, string newName )
        {
            var group   = mod._groups[ groupIdx ];
            var oldName = group.Name;
            if( oldName == newName || !VerifyFileName( mod, group, newName, true ) )
            {
                return;
            }

            var _ = group switch
            {
                SingleModGroup s => s.Name = newName,
                MultiModGroup m  => m.Name = newName,
                _                => newName,
            };

            ModOptionChanged.Invoke( ModOptionChangeType.GroupRenamed, mod, groupIdx, -1, -1 );
        }

        public void AddModGroup( Mod mod, SelectType type, string newName )
        {
            if( !VerifyFileName( mod, null, newName, true ) )
            {
                return;
            }

            var maxPriority = mod._groups.Count == 0 ? 0 : mod._groups.Max( o => o.Priority ) + 1;

            mod._groups.Add( type == SelectType.Multi
                ? new MultiModGroup { Name  = newName, Priority = maxPriority }
                : new SingleModGroup { Name = newName, Priority = maxPriority } );
            ModOptionChanged.Invoke( ModOptionChangeType.GroupAdded, mod, mod._groups.Count - 1, -1, -1 );
        }

        public void DeleteModGroup( Mod mod, int groupIdx )
        {
            var group = mod._groups[ groupIdx ];
            mod._groups.RemoveAt( groupIdx );
            group.DeleteFile( mod.BasePath );
            ModOptionChanged.Invoke( ModOptionChangeType.GroupDeleted, mod, groupIdx, -1, -1 );
        }

        public void MoveModGroup( Mod mod, int groupIdxFrom, int groupIdxTo )
        {
            if( mod._groups.Move( groupIdxFrom, groupIdxTo ) )
            {
                ModOptionChanged.Invoke( ModOptionChangeType.GroupMoved, mod, groupIdxFrom, -1, groupIdxTo );
            }
        }

        public void ChangeGroupDescription( Mod mod, int groupIdx, string newDescription )
        {
            var group = mod._groups[ groupIdx ];
            if( group.Description == newDescription )
            {
                return;
            }

            var _ = group switch
            {
                SingleModGroup s => s.Description = newDescription,
                MultiModGroup m  => m.Description = newDescription,
                _                => newDescription,
            };
            ModOptionChanged.Invoke( ModOptionChangeType.DisplayChange, mod, groupIdx, -1, -1 );
        }

        public void ChangeGroupPriority( Mod mod, int groupIdx, int newPriority )
        {
            var group = mod._groups[ groupIdx ];
            if( group.Priority == newPriority )
            {
                return;
            }

            var _ = group switch
            {
                SingleModGroup s => s.Priority = newPriority,
                MultiModGroup m  => m.Priority = newPriority,
                _                => newPriority,
            };
            ModOptionChanged.Invoke( ModOptionChangeType.PriorityChanged, mod, groupIdx, -1, -1 );
        }

        public void ChangeOptionPriority( Mod mod, int groupIdx, int optionIdx, int newPriority )
        {
            switch( mod._groups[ groupIdx ] )
            {
                case SingleModGroup:
                    ChangeGroupPriority( mod, groupIdx, newPriority );
                    break;
                case MultiModGroup m:
                    if( m.PrioritizedOptions[ optionIdx ].Priority == newPriority )
                    {
                        return;
                    }

                    m.PrioritizedOptions[ optionIdx ] = ( m.PrioritizedOptions[ optionIdx ].Mod, newPriority );
                    ModOptionChanged.Invoke( ModOptionChangeType.PriorityChanged, mod, groupIdx, optionIdx, -1 );
                    return;
            }
        }

        public void RenameOption( Mod mod, int groupIdx, int optionIdx, string newName )
        {
            switch( mod._groups[ groupIdx ] )
            {
                case SingleModGroup s:
                    if( s.OptionData[ optionIdx ].Name == newName )
                    {
                        return;
                    }

                    s.OptionData[ optionIdx ].Name = newName;
                    break;
                case MultiModGroup m:
                    var option = m.PrioritizedOptions[ optionIdx ].Mod;
                    if( option.Name == newName )
                    {
                        return;
                    }

                    option.Name = newName;
                    return;
            }

            ModOptionChanged.Invoke( ModOptionChangeType.DisplayChange, mod, groupIdx, optionIdx, -1 );
        }

        public void AddOption( Mod mod, int groupIdx, string newName )
        {
            switch( mod._groups[ groupIdx ] )
            {
                case SingleModGroup s:
                    s.OptionData.Add( new SubMod { Name = newName } );
                    break;
                case MultiModGroup m:
                    m.PrioritizedOptions.Add( ( new SubMod { Name = newName }, 0 ) );
                    break;
            }

            ModOptionChanged.Invoke( ModOptionChangeType.OptionAdded, mod, groupIdx, mod._groups[ groupIdx ].Count - 1, -1 );
        }

        public void AddOption( Mod mod, int groupIdx, ISubMod option, int priority = 0 )
        {
            if( option is not SubMod o )
            {
                return;
            }

            if( mod._groups[ groupIdx ].Count > 63 )
            {
                PluginLog.Error(
                    $"Could not add option {option.Name} to {mod._groups[ groupIdx ].Name} for mod {mod.Name}, "
                  + "since only up to 64 options are supported in one group." );
                return;
            }

            switch( mod._groups[ groupIdx ] )
            {
                case SingleModGroup s:
                    s.OptionData.Add( o );
                    break;
                case MultiModGroup m:
                    m.PrioritizedOptions.Add( ( o, priority ) );
                    break;
            }

            ModOptionChanged.Invoke( ModOptionChangeType.OptionAdded, mod, groupIdx, mod._groups[ groupIdx ].Count - 1, -1 );
        }

        public void DeleteOption( Mod mod, int groupIdx, int optionIdx )
        {
            switch( mod._groups[ groupIdx ] )
            {
                case SingleModGroup s:
                    s.OptionData.RemoveAt( optionIdx );
                    break;
                case MultiModGroup m:
                    m.PrioritizedOptions.RemoveAt( optionIdx );
                    break;
            }

            ModOptionChanged.Invoke( ModOptionChangeType.OptionDeleted, mod, groupIdx, optionIdx, -1 );
        }

        public void MoveOption( Mod mod, int groupIdx, int optionIdxFrom, int optionIdxTo )
        {
            var group = mod._groups[ groupIdx ];
            if( group.MoveOption( optionIdxFrom, optionIdxTo ) )
            {
                ModOptionChanged.Invoke( ModOptionChangeType.OptionMoved, mod, groupIdx, optionIdxFrom, optionIdxTo );
            }
        }

        public void OptionSetManipulation( Mod mod, int groupIdx, int optionIdx, MetaManipulation manip, bool delete = false )
        {
            var subMod = GetSubMod( mod, groupIdx, optionIdx );
            if( delete )
            {
                if( !subMod.ManipulationData.Remove( manip ) )
                {
                    return;
                }
            }
            else
            {
                if( subMod.ManipulationData.TryGetValue( manip, out var oldManip ) )
                {
                    if( manip.EntryEquals( oldManip ) )
                    {
                        return;
                    }

                    subMod.ManipulationData.Remove( oldManip );
                    subMod.ManipulationData.Add( manip );
                }
                else
                {
                    subMod.ManipulationData.Add( manip );
                }
            }

            ModOptionChanged.Invoke( ModOptionChangeType.OptionMetaChanged, mod, groupIdx, optionIdx, -1 );
        }

        public void OptionSetManipulations( Mod mod, int groupIdx, int optionIdx, HashSet< MetaManipulation > manipulations )
        {
            var subMod = GetSubMod( mod, groupIdx, optionIdx );
            if( subMod.Manipulations.SetEquals( manipulations ) )
            {
                return;
            }

            subMod.ManipulationData.Clear();
            subMod.ManipulationData.UnionWith( manipulations );
            ModOptionChanged.Invoke( ModOptionChangeType.OptionMetaChanged, mod, groupIdx, optionIdx, -1 );
        }

        public void OptionSetFile( Mod mod, int groupIdx, int optionIdx, Utf8GamePath gamePath, FullPath? newPath )
        {
            var subMod = GetSubMod( mod, groupIdx, optionIdx );
            if( OptionSetFile( subMod.FileData, gamePath, newPath ) )
            {
                ModOptionChanged.Invoke( ModOptionChangeType.OptionFilesChanged, mod, groupIdx, optionIdx, -1 );
            }
        }

        public void OptionSetFiles( Mod mod, int groupIdx, int optionIdx, Dictionary< Utf8GamePath, FullPath > replacements )
        {
            var subMod = GetSubMod( mod, groupIdx, optionIdx );
            if( subMod.FileData.Equals( replacements ) )
            {
                return;
            }

            subMod.FileData.SetTo( replacements );
            ModOptionChanged.Invoke( ModOptionChangeType.OptionFilesChanged, mod, groupIdx, optionIdx, -1 );
        }

        public void OptionSetFileSwap( Mod mod, int groupIdx, int optionIdx, Utf8GamePath gamePath, FullPath? newPath )
        {
            var subMod = GetSubMod( mod, groupIdx, optionIdx );
            if( OptionSetFile( subMod.FileSwapData, gamePath, newPath ) )
            {
                ModOptionChanged.Invoke( ModOptionChangeType.OptionSwapsChanged, mod, groupIdx, optionIdx, -1 );
            }
        }

        public void OptionSetFileSwaps( Mod mod, int groupIdx, int optionIdx, Dictionary< Utf8GamePath, FullPath > swaps )
        {
            var subMod = GetSubMod( mod, groupIdx, optionIdx );
            if( subMod.FileSwapData.Equals( swaps ) )
            {
                return;
            }

            subMod.FileSwapData.SetTo( swaps );
            ModOptionChanged.Invoke( ModOptionChangeType.OptionSwapsChanged, mod, groupIdx, optionIdx, -1 );
        }

        public void OptionUpdate( Mod mod, int groupIdx, int optionIdx, Dictionary< Utf8GamePath, FullPath > replacements,
            HashSet< MetaManipulation > manipulations, Dictionary< Utf8GamePath, FullPath > swaps )
        {
            var subMod = GetSubMod( mod, groupIdx, optionIdx );
            subMod.FileData.SetTo( replacements );
            subMod.ManipulationData.Clear();
            subMod.ManipulationData.UnionWith( manipulations );
            subMod.FileSwapData.SetTo( swaps );
            ModOptionChanged.Invoke( ModOptionChangeType.OptionUpdated, mod, groupIdx, optionIdx, -1 );
        }

        public static bool VerifyFileName( Mod mod, IModGroup? group, string newName, bool message )
        {
            var path = newName.RemoveInvalidPathSymbols();
            if( path.Length == 0
            || mod.Groups.Any( o => !ReferenceEquals( o, group )
                && string.Equals( o.Name.RemoveInvalidPathSymbols(), path, StringComparison.InvariantCultureIgnoreCase ) ) )
            {
                if( message )
                {
                    PluginLog.Warning( $"Could not name option {newName} because option with same filename {path} already exists." );
                }

                return false;
            }

            return true;
        }

        private static SubMod GetSubMod( Mod mod, int groupIdx, int optionIdx )
        {
            return mod._groups[ groupIdx ] switch
            {
                SingleModGroup s => s.OptionData[ optionIdx ],
                MultiModGroup m  => m.PrioritizedOptions[ optionIdx ].Mod,
                _                => throw new InvalidOperationException(),
            };
        }

        private static bool OptionSetFile( IDictionary< Utf8GamePath, FullPath > dict, Utf8GamePath gamePath, FullPath? newPath )
        {
            if( dict.TryGetValue( gamePath, out var oldPath ) )
            {
                if( newPath == null )
                {
                    dict.Remove( gamePath );
                    return true;
                }

                if( newPath.Value.Equals( oldPath ) )
                {
                    return false;
                }

                dict[ gamePath ] = newPath.Value;
                return true;
            }

            if( newPath == null )
            {
                return false;
            }

            dict.Add( gamePath, newPath.Value );
            return true;
        }

        private static void OnModOptionChange( ModOptionChangeType type, Mod mod, int groupIdx, int _, int _2 )
        {
            // File deletion is handled in the actual function.
            if( type != ModOptionChangeType.GroupDeleted )
            {
                IModGroup.SaveModGroup( mod._groups[ groupIdx ], mod.BasePath );
            }

            // State can not change on adding groups, as they have no immediate options.
            mod.HasOptions = type switch
            {
                ModOptionChangeType.GroupDeleted     => mod.HasOptions =  mod.Groups.Any( o => o.IsOption ),
                ModOptionChangeType.GroupTypeChanged => mod.HasOptions =  mod.Groups.Any( o => o.IsOption ),
                ModOptionChangeType.OptionAdded      => mod.HasOptions |= mod._groups[ groupIdx ].IsOption,
                ModOptionChangeType.OptionDeleted    => mod.HasOptions =  mod.Groups.Any( o => o.IsOption ),
                _                                    => mod.HasOptions,
            };
        }
    }
}