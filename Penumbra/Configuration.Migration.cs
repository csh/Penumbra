using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OtterGui.Filesystem;
using Penumbra.Collections;
using Penumbra.Mods;

namespace Penumbra;

public partial class Configuration
{
    // Contains everything to migrate from older versions of the config to the current,
    // including deprecated fields.
    private class Migration
    {
        private Configuration _config = null!;
        private JObject       _data   = null!;

        public string                       CurrentCollection    = ModCollection.DefaultCollection;
        public string                       DefaultCollection    = ModCollection.DefaultCollection;
        public string                       ForcedCollection     = string.Empty;
        public Dictionary< string, string > CharacterCollections = new();
        public Dictionary< string, string > ModSortOrder         = new();
        public bool                         InvertModListOrder;
        public bool                         SortFoldersFirst;
        public SortModeV3                   SortMode = SortModeV3.FoldersFirst;

        public static void Migrate( Configuration config )
        {
            if( !File.Exists( Dalamud.PluginInterface.ConfigFile.FullName ) )
            {
                return;
            }

            var m = new Migration
            {
                _config = config,
                _data   = JObject.Parse( File.ReadAllText( Dalamud.PluginInterface.ConfigFile.FullName ) ),
            };

            CreateBackup();
            m.Version0To1();
            m.Version1To2();
            m.Version2To3();
            m.Version3To4();
        }

        // SortMode was changed from an enum to a type.
        private void Version3To4()
        {
            if( _config.Version != 3 )
            {
                return;
            }

            SortMode = _data[ nameof( SortMode ) ]?.ToObject< SortModeV3 >() ?? SortMode;
            _config.SortMode = SortMode switch
            {
                SortModeV3.FoldersFirst           => ISortMode< Mod >.FoldersFirst,
                SortModeV3.Lexicographical        => ISortMode< Mod >.Lexicographical,
                SortModeV3.InverseFoldersFirst    => ISortMode< Mod >.InverseFoldersFirst,
                SortModeV3.InverseLexicographical => ISortMode< Mod >.InverseLexicographical,
                SortModeV3.FoldersLast            => ISortMode< Mod >.FoldersLast,
                SortModeV3.InverseFoldersLast     => ISortMode< Mod >.InverseFoldersLast,
                SortModeV3.InternalOrder          => ISortMode< Mod >.InternalOrder,
                SortModeV3.InternalOrderInverse   => ISortMode< Mod >.InverseInternalOrder,
                _                                 => ISortMode< Mod >.FoldersFirst,
            };
            _config.Version = 4;
        }

        // SortFoldersFirst was changed from a bool to the enum SortMode.
        private void Version2To3()
        {
            if( _config.Version != 2 )
            {
                return;
            }

            SortFoldersFirst = _data[ nameof( SortFoldersFirst ) ]?.ToObject< bool >() ?? false;
            SortMode         = SortFoldersFirst ? SortModeV3.FoldersFirst : SortModeV3.Lexicographical;
            _config.Version  = 3;
        }

        // The forced collection was removed due to general inheritance.
        // Sort Order was moved to a separate file and may contain empty folders.
        // Active collections in general were moved to their own file.
        // Delete the penumbrametatmp folder if it exists.
        private void Version1To2()
        {
            if( _config.Version != 1 )
            {
                return;
            }

            // Ensure the right meta files are loaded.
            DeleteMetaTmp();
            Penumbra.CharacterUtility.LoadCharacterResources();
            ResettleSortOrder();
            ResettleCollectionSettings();
            ResettleForcedCollection();
            _config.Version = 2;
        }

        private void DeleteMetaTmp()
        {
            var path = Path.Combine( _config.ModDirectory, "penumbrametatmp" );
            if( Directory.Exists( path ) )
            {
                try
                {
                    Directory.Delete( path, true );
                }
                catch( Exception e )
                {
                    PluginLog.Error( $"Could not delete the outdated penumbrametatmp folder:\n{e}" );
                }
            }
        }

        private void ResettleForcedCollection()
        {
            ForcedCollection = _data[ nameof( ForcedCollection ) ]?.ToObject< string >() ?? ForcedCollection;
            if( ForcedCollection.Length <= 0 )
            {
                return;
            }

            // Add the previous forced collection to all current collections except itself as an inheritance.
            foreach( var collection in Directory.EnumerateFiles( ModCollection.CollectionDirectory, "*.json" ) )
            {
                try
                {
                    var jObject = JObject.Parse( File.ReadAllText( collection ) );
                    if( jObject[ nameof( ModCollection.Name ) ]?.ToObject< string >() != ForcedCollection )
                    {
                        jObject[ nameof( ModCollection.Inheritance ) ] = JToken.FromObject( new List< string >() { ForcedCollection } );
                        File.WriteAllText( collection, jObject.ToString() );
                    }
                }
                catch( Exception e )
                {
                    PluginLog.Error(
                        $"Could not transfer forced collection {ForcedCollection} to inheritance of collection {collection}:\n{e}" );
                }
            }
        }

        // Move the current sort order to its own file.
        private void ResettleSortOrder()
        {
            ModSortOrder = _data[ nameof( ModSortOrder ) ]?.ToObject< Dictionary< string, string > >() ?? ModSortOrder;
            var       file   = ModFileSystem.ModFileSystemFile;
            using var stream = File.Open( file, File.Exists( file ) ? FileMode.Truncate : FileMode.CreateNew );
            using var writer = new StreamWriter( stream );
            using var j      = new JsonTextWriter( writer );
            j.Formatting = Formatting.Indented;
            j.WriteStartObject();
            j.WritePropertyName( "Data" );
            j.WriteStartObject();
            foreach( var (mod, path) in ModSortOrder.Where( kvp => Directory.Exists( Path.Combine( _config.ModDirectory, kvp.Key ) ) ) )
            {
                j.WritePropertyName( mod, true );
                j.WriteValue( path );
            }

            j.WriteEndObject();
            j.WritePropertyName( "EmptyFolders" );
            j.WriteStartArray();
            j.WriteEndArray();
            j.WriteEndObject();
        }

        // Move the active collections to their own file.
        private void ResettleCollectionSettings()
        {
            CurrentCollection    = _data[ nameof( CurrentCollection ) ]?.ToObject< string >()                          ?? CurrentCollection;
            DefaultCollection    = _data[ nameof( DefaultCollection ) ]?.ToObject< string >()                          ?? DefaultCollection;
            CharacterCollections = _data[ nameof( CharacterCollections ) ]?.ToObject< Dictionary< string, string > >() ?? CharacterCollections;
            ModCollection.Manager.SaveActiveCollections( DefaultCollection, CurrentCollection,
                CharacterCollections.Select( kvp => ( kvp.Key, kvp.Value ) ), Array.Empty< (CollectionType, string) >() );
        }

        // Collections were introduced and the previous CurrentCollection got put into ModDirectory.
        private void Version0To1()
        {
            if( _config.Version != 0 )
            {
                return;
            }

            _config.ModDirectory = _data[ nameof( CurrentCollection ) ]?.ToObject< string >() ?? string.Empty;
            _config.Version      = 1;
            ResettleCollectionJson();
        }

        // Move the previous mod configurations to a new default collection file.
        private void ResettleCollectionJson()
        {
            var collectionJson = new FileInfo( Path.Combine( _config.ModDirectory, "collection.json" ) );
            if( !collectionJson.Exists )
            {
                return;
            }

            var defaultCollection     = ModCollection.CreateNewEmpty( ModCollection.DefaultCollection );
            var defaultCollectionFile = defaultCollection.FileName;
            if( defaultCollectionFile.Exists )
            {
                return;
            }

            try
            {
                var text = File.ReadAllText( collectionJson.FullName );
                var data = JArray.Parse( text );

                var maxPriority = 0;
                var dict        = new Dictionary< string, ModSettings.SavedSettings >();
                foreach( var setting in data.Cast< JObject >() )
                {
                    var modName  = ( string )setting[ "FolderName" ]!;
                    var enabled  = ( bool )setting[ "Enabled" ]!;
                    var priority = ( int )setting[ "Priority" ]!;
                    var settings = setting[ "Settings" ]!.ToObject< Dictionary< string, long > >()
                     ?? setting[ "Conf" ]!.ToObject< Dictionary< string, long > >();

                    dict[ modName ] = new ModSettings.SavedSettings()
                    {
                        Enabled  = enabled,
                        Priority = priority,
                        Settings = settings!,
                    };
                    maxPriority = Math.Max( maxPriority, priority );
                }

                InvertModListOrder = _data[ nameof( InvertModListOrder ) ]?.ToObject< bool >() ?? InvertModListOrder;
                if( !InvertModListOrder )
                {
                    dict = dict.ToDictionary( kvp => kvp.Key, kvp => kvp.Value with { Priority = maxPriority - kvp.Value.Priority } );
                }

                defaultCollection = ModCollection.MigrateFromV0( ModCollection.DefaultCollection, dict );
                defaultCollection.Save();
            }
            catch( Exception e )
            {
                PluginLog.Error( $"Could not migrate the old collection file to new collection files:\n{e}" );
                throw;
            }
        }

        // Create a backup of the configuration file specifically.
        private static void CreateBackup()
        {
            var name    = Dalamud.PluginInterface.ConfigFile.FullName;
            var bakName = name + ".bak";
            try
            {
                File.Copy( name, bakName, true );
            }
            catch( Exception e )
            {
                PluginLog.Error( $"Could not create backup copy of config at {bakName}:\n{e}" );
            }
        }

        public enum SortModeV3 : byte
        {
            FoldersFirst           = 0x00,
            Lexicographical        = 0x01,
            InverseFoldersFirst    = 0x02,
            InverseLexicographical = 0x03,
            FoldersLast            = 0x04,
            InverseFoldersLast     = 0x05,
            InternalOrder          = 0x06,
            InternalOrderInverse   = 0x07,
        }
    }
}