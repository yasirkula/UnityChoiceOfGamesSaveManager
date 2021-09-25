using SimpleFileBrowser;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	[Serializable]
	public class CachedSaveFilePaths
	{
		public string[] paths;
	}

	public class SaveManager : MonoBehaviour
	{
		private const string GAME_TITLE_FORMAT = "- {0} -";

#pragma warning disable 0649
		[SerializeField]
		private float saveCheckInterval = 0.1f;
		private float nextAutomatedSaveCheckTime;

		[SerializeField]
		private InputField gameSaveDirectoryInputField, outputDirectoryInputField, manualSaveNameInputField, numberOfAutomatedSavesInputField;

		[SerializeField]
		private Button pickGameSaveDirectoryButton, pickOutputDirectoryButton, saveButton;

		[SerializeField]
		private Dropdown manualSaveNamesDropdown;

		[SerializeField]
		private SavesScrollView manualSavesScrollView, automatedSavesScrollView;

		[SerializeField]
		private Text gameTitleText;

		[SerializeField]
		private Button gameTitleBackgroundButton;

		[SerializeField]
		private SaveEditorWindow saveEditorWindow;

		[SerializeField]
		private LoadConfirmationDialog loadConfirmationDialog;

		[SerializeField]
		private SaveOverwriteDialog saveOverwriteDialog;

		[SerializeField]
		private SaveFileSelectionDialog saveFileSelectionDialog;

		[SerializeField]
		private SwitchGameDialog switchGameDialog;

		[SerializeField]
		private ReducedAutomatedSaveCountDialog reducedAutomatedSaveCountDialog;

		[SerializeField]
		private Color gameSaveDirectoryInvalidColor;
		private Color gameSaveDirectoryValidColor;
		private Image gameSaveDirectoryBackground;
#pragma warning restore 0649

		private string m_gameSaveDirectory;
		private string GameSaveDirectory
		{
			get
			{
				if( m_gameSaveDirectory == null )
				{
					m_gameSaveDirectory = PlayerPrefs.GetString( "GamePath", "" );
					if( m_gameSaveDirectory.Length > 0 && string.IsNullOrEmpty( GameSaveFilePath ) )
						gameSaveDirectoryBackground.color = gameSaveDirectoryInvalidColor;
				}

				return m_gameSaveDirectory;
			}
			set
			{
				// We should call the setter even if value doesn't change because we want to show saveFileSelectionDialog if there are multiple save files in the directory
				if( /*m_gameSaveDirectory != value &&*/ !string.IsNullOrEmpty( value ) )
				{
					m_gameSaveDirectory = value;
					gameSaveDirectoryInputField.text = value.ToString();

					// There can be multiple save files in one directory (e.g. 'Heroes Rise' series stores all save files in the same directory)
					// In that case, we will prompt the user to manually select the desired save file that will be tracked for automated saves
					string[] gameSaveFilePaths = GetAllSaveFilePaths( value );
					gameSaveDirectoryBackground.color = gameSaveFilePaths.Length > 0 ? gameSaveDirectoryValidColor : gameSaveDirectoryInvalidColor;

					if( gameSaveFilePaths.Length == 0 )
						GameSaveFilePath = "";
					else if( gameSaveFilePaths.Length == 1 )
						GameSaveFilePath = gameSaveFilePaths[0];
					else
					{
						GameSaveFilePath = "";

						saveFileSelectionDialog.Show( gameSaveFilePaths, GameSaveFilePath, ( selectedSaveFilePath ) =>
						{
							GameSaveFilePath = selectedSaveFilePath;
							LoadSaveFiles();
						}, false );
					}

					PlayerPrefs.SetString( "GamePath", value );
					PlayerPrefs.Save();

					LoadSaveFiles();
				}
			}
		}

		private string m_gameSaveFilePath;
		private string GameSaveFilePath
		{
			get
			{
				if( m_gameSaveFilePath == null )
				{
					m_gameSaveFilePath = PlayerPrefs.GetString( "GameSaveFilePath", "" );
					if( !string.IsNullOrEmpty( m_gameSaveFilePath ) && !File.Exists( m_gameSaveFilePath ) )
						m_gameSaveFilePath = "";

					gameTitleText.text = ( string.IsNullOrEmpty( m_gameSaveFilePath ) || !File.Exists( m_gameSaveFilePath ) ) ? "No Choice of Game selected" : string.Format( GAME_TITLE_FORMAT, GetReadableSaveFileName( m_gameSaveFilePath ) );
				}

				return m_gameSaveFilePath;
			}
			set
			{
				if( m_gameSaveFilePath != value )
				{
					m_gameSaveFilePath = value;
					gameTitleText.text = ( string.IsNullOrEmpty( value ) || !File.Exists( value ) ) ? "No Choice of Game selected" : string.Format( GAME_TITLE_FORMAT, GetReadableSaveFileName( value ) );

					PlayerPrefs.SetString( "GameSaveFilePath", value );
					PlayerPrefs.Save();
				}
			}
		}

		private string m_outputDirectory, manualSavesDirectory, automatedSavesDirectory;
		private string OutputDirectory
		{
			get
			{
				if( m_outputDirectory == null )
				{
					m_outputDirectory = PlayerPrefs.GetString( "OutputPath", "" );
					if( m_outputDirectory.Length == 0 )
					{
						m_outputDirectory = Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ) ?? "";
#if UNITY_STANDALONE_OSX
						// Documents folder must be appended manually on Mac OS
						if( m_outputDirectory.Length > 0 )
							m_outputDirectory = Path.Combine( m_outputDirectory, "Documents" );
#endif

						if( m_outputDirectory.Length > 0 )
							m_outputDirectory = Path.Combine( m_outputDirectory, "CoG Saves" );
					}
				}

				return m_outputDirectory;
			}
			set
			{
				if( m_outputDirectory != value && !string.IsNullOrEmpty( value ) )
				{
					m_outputDirectory = value;
					outputDirectoryInputField.text = value.ToString();

					PlayerPrefs.SetString( "OutputPath", value );
					PlayerPrefs.Save();

					LoadSaveFiles();
				}
			}
		}

		private string m_manualSaveName;
		private string ManualSaveName
		{
			get
			{
				if( m_manualSaveName == null )
					m_manualSaveName = PlayerPrefs.GetString( "ManualSaveName", "My Save" );

				return m_manualSaveName;
			}
			set
			{
				if( m_manualSaveName != value && !string.IsNullOrEmpty( value ) )
				{
					m_manualSaveName = value;
					manualSaveNameInputField.text = value.ToString();

					PlayerPrefs.SetString( "ManualSaveName", value );
					PlayerPrefs.Save();
				}
			}
		}

		private int? m_numberOfAutomatedSaves;
		private int NumberOfAutomatedSaves
		{
			get
			{
				if( !m_numberOfAutomatedSaves.HasValue )
					m_numberOfAutomatedSaves = PlayerPrefs.GetInt( "AutoSaveCount", 25 );

				return m_numberOfAutomatedSaves.Value;
			}
			set
			{
				if( m_numberOfAutomatedSaves != value && value >= 0 )
				{
					m_numberOfAutomatedSaves = value;
					numberOfAutomatedSavesInputField.text = value.ToString();

					PlayerPrefs.SetInt( "AutoSaveCount", value );
					PlayerPrefs.Save();
				}
			}
		}

		private readonly DynamicCircularBuffer<SaveEntry> manualSaves = new DynamicCircularBuffer<SaveEntry>( 64 );
		private readonly DynamicCircularBuffer<SaveEntry> automatedSaves = new DynamicCircularBuffer<SaveEntry>( 64 );
		private readonly HashSet<string> automatedSaveDirectoryNames = new HashSet<string>();

		private string[] invalidFilenameChars;

		private DateTime manuallyLoadedGameDateTime;
		public static DateTime CurrentlyActiveSaveDateTime;

		private readonly HashSet<string> exploredGameSaveFilePaths = new HashSet<string>();

		private void Awake()
		{
			gameSaveDirectoryBackground = gameSaveDirectoryInputField.GetComponent<Image>();
			gameSaveDirectoryValidColor = gameSaveDirectoryBackground.color;

			gameSaveDirectoryInputField.text = GameSaveDirectory;
			outputDirectoryInputField.text = OutputDirectory;
			manualSaveNameInputField.text = ManualSaveName;
			numberOfAutomatedSavesInputField.text = NumberOfAutomatedSaves.ToString();

			char[] _invalidFilenameChars = Path.GetInvalidFileNameChars();
			invalidFilenameChars = new string[_invalidFilenameChars.Length];
			for( int i = 0; i < _invalidFilenameChars.Length; i++ )
				invalidFilenameChars[i] = _invalidFilenameChars[i].ToString();

			manualSaveNameInputField.onEndEdit.AddListener( ( value ) =>
			{
				// Strip save name of invalid filename characters
				value = value.Trim();
				foreach( string invalidFilenameChar in invalidFilenameChars )
					value = value.Replace( invalidFilenameChar, "" );

				if( value.Length > 0 )
					ManualSaveName = value;
				else
					manualSaveNameInputField.text = ManualSaveName;
			} );

			numberOfAutomatedSavesInputField.onEndEdit.AddListener( ( value ) =>
			{
				int newSaveCount;
				if( int.TryParse( value, out newSaveCount ) && newSaveCount >= 0 )
				{
					if( newSaveCount >= automatedSaves.Count )
						NumberOfAutomatedSaves = newSaveCount;
					else
					{
						reducedAutomatedSaveCountDialog.Show( newSaveCount, automatedSaves.Count, () =>
						{
							NumberOfAutomatedSaves = newSaveCount;

							while( automatedSaves.Count > newSaveCount )
							{
								SaveEntry saveEntry = automatedSaves.RemoveFirst();

								Directory.Delete( saveEntry.directory, true );
								automatedSaveDirectoryNames.Remove( Path.GetFileName( saveEntry.directory ) );
							}

							automatedSavesScrollView.UpdateList();
						},
						() => numberOfAutomatedSavesInputField.text = NumberOfAutomatedSaves.ToString() );
					}
				}
				else
					numberOfAutomatedSavesInputField.text = NumberOfAutomatedSaves.ToString();
			} );

			pickGameSaveDirectoryButton.onClick.AddListener( () =>
			{
				string initialPath = "";
				if( !string.IsNullOrEmpty( GameSaveFilePath ) && File.Exists( GameSaveFilePath ) )
					initialPath = Path.GetDirectoryName( GameSaveDirectory );
				else
				{
					string steamSaveDirectory = GetSteamSavesDirectory();
					if( !string.IsNullOrEmpty( steamSaveDirectory ) )
					{
						string[] steamUsers = Directory.GetDirectories( steamSaveDirectory );
						initialPath = ( steamUsers.Length == 1 ) ? steamUsers[0] : steamSaveDirectory;
					}
				}

				FileBrowser.ShowLoadDialog( ( paths ) => GameSaveDirectory = paths[0], null, FileBrowser.PickMode.Folders, initialPath: initialPath, title: "Select the target Choice of Games' save directory" );
			} );

			pickOutputDirectoryButton.onClick.AddListener( () => FileBrowser.ShowSaveDialog( ( paths ) => OutputDirectory = paths[0], null, FileBrowser.PickMode.Folders, title: "Select the folder where the save files will be stored", initialPath: OutputDirectory, saveButtonText: "Select" ) );

			saveButton.onClick.AddListener( () =>
			{
				if( !string.IsNullOrEmpty( GameSaveFilePath ) && !string.IsNullOrEmpty( manualSavesDirectory ) && !string.IsNullOrEmpty( ManualSaveName ) && File.Exists( GameSaveFilePath ) )
				{
					if( manualSaves.Find( ( saveEntry ) => saveEntry.saveName == ManualSaveName ) != null )
						saveOverwriteDialog.Show( ManualSaveName, SaveGameManual );
					else
						SaveGameManual();
				}
			} );

			gameTitleBackgroundButton.onClick.AddListener( () =>
			{
				if( exploredGameSaveFilePaths.Count > 0 )
				{
					string[] _exploredGameSaveFilePaths = new string[exploredGameSaveFilePaths.Count];
					exploredGameSaveFilePaths.CopyTo( _exploredGameSaveFilePaths );
					Array.Sort( _exploredGameSaveFilePaths, ( path1, path2 ) => Path.GetFileName( path1 ).CompareTo( Path.GetFileName( path2 ) ) );

					switchGameDialog.Show( _exploredGameSaveFilePaths, GameSaveFilePath, ( selectedSaveFilePath ) =>
					{
						GameSaveDirectory = Path.GetDirectoryName( Path.GetDirectoryName( selectedSaveFilePath ) );
						GameSaveFilePath = selectedSaveFilePath;
						LoadSaveFiles();

						// If changing the value of GameSaveDirectory prompted saveFileSelectionDialog, close it because we've assigned GameSaveFilePath's value manually
						saveFileSelectionDialog.gameObject.SetActive( false );
					}, true );
				}
			} );

			saveEditorWindow.OnSaveEntryModified += ( modifiedSaveEntry ) =>
			{
				// If the currently active save is edited, reload the save
				if( modifiedSaveEntry.saveDate == CurrentlyActiveSaveDateTime )
					LoadGame( modifiedSaveEntry.directory );
			};

			ManualSaveNameDropdownEntry.OnSelectionChanged = ( saveName ) => ManualSaveName = saveName;

			manualSavesScrollView.OnSaveVisualClicked += ( saveEntryVisual ) => OnSaveEntryClicked( saveEntryVisual.SaveEntry, false );
			automatedSavesScrollView.OnSaveVisualClicked += ( saveEntryVisual ) => OnSaveEntryClicked( saveEntryVisual.SaveEntry, true );

			manualSavesScrollView.Initialize( manualSaves );
			automatedSavesScrollView.Initialize( automatedSaves );

			try
			{
				bool exploredGameSaveFilePathsChanged = false;
				CachedSaveFilePaths cachedSaveFilePaths = JsonUtility.FromJson<CachedSaveFilePaths>( PlayerPrefs.GetString( "CachedSaveFilePaths", "{}" ) );
				if( cachedSaveFilePaths.paths != null )
				{
					exploredGameSaveFilePaths.UnionWith( cachedSaveFilePaths.paths );

					// Remove cached save file paths that no longer exist
					if( exploredGameSaveFilePaths.RemoveWhere( ( cachedSaveFilePath ) => !File.Exists( cachedSaveFilePath ) ) > 0 )
						exploredGameSaveFilePathsChanged = true;
				}

				// Automatically fetch the list of Choice of Games from Steam saves folder at each launch
				string steamSaveDirectory = GetSteamSavesDirectory();
				if( !string.IsNullOrEmpty( steamSaveDirectory ) )
				{
					foreach( string potentialSaveFilePath in Directory.GetFiles( steamSaveDirectory, "*PSstate", SearchOption.AllDirectories ) )
					{
						if( potentialSaveFilePath.EndsWith( "PSstate" ) && Path.GetFileName( Path.GetDirectoryName( potentialSaveFilePath ) ) == "remote" && exploredGameSaveFilePaths.Add( potentialSaveFilePath ) )
							exploredGameSaveFilePathsChanged = true;
					}
				}

				if( exploredGameSaveFilePathsChanged )
				{
					string[] _exploredGameSaveFilePaths = new string[exploredGameSaveFilePaths.Count];
					exploredGameSaveFilePaths.CopyTo( _exploredGameSaveFilePaths );

					PlayerPrefs.SetString( "CachedSaveFilePaths", JsonUtility.ToJson( new CachedSaveFilePaths() { paths = _exploredGameSaveFilePaths } ) );
					PlayerPrefs.Save();
				}
			}
			catch( Exception e )
			{
				Debug.LogException( e );
			}

			LoadSaveFiles();

			nextAutomatedSaveCheckTime = Time.time + saveCheckInterval;
			Application.targetFrameRate = 60;
		}

		private void Update()
		{
			if( Time.time > nextAutomatedSaveCheckTime )
			{
				nextAutomatedSaveCheckTime = Time.time + saveCheckInterval;

				if( !string.IsNullOrEmpty( GameSaveFilePath ) && File.Exists( GameSaveFilePath ) )
				{
					DateTime _currentlyActiveSaveDateTime = File.GetLastWriteTime( GameSaveFilePath );
					if( CurrentlyActiveSaveDateTime != _currentlyActiveSaveDateTime )
					{
						CurrentlyActiveSaveDateTime = _currentlyActiveSaveDateTime;

						// When currently active save changes, we need to refresh the lists so that manual/automated saves with matching save dates can change their color
						manualSavesScrollView.UpdateList();
						automatedSavesScrollView.UpdateList();
					}

					if( NumberOfAutomatedSaves > 0 && !string.IsNullOrEmpty( automatedSavesDirectory ) && _currentlyActiveSaveDateTime != manuallyLoadedGameDateTime && ( automatedSaves.Count == 0 || _currentlyActiveSaveDateTime > automatedSaves[automatedSaves.Count - 1].saveDate ) )
						SaveGameAutomated();
				}
				else
					CurrentlyActiveSaveDateTime = new DateTime();
			}
		}

		private void LoadSaveFiles()
		{
			manualSaves.Clear();
			automatedSaves.Clear();

			manualSavesDirectory = "";
			automatedSavesDirectory = "";

			automatedSaveDirectoryNames.Clear();

			manuallyLoadedGameDateTime = new DateTime();
			DateTime gameSaveFileDateTime = new DateTime();

			if( !string.IsNullOrEmpty( GameSaveFilePath ) && File.Exists( GameSaveFilePath ) )
			{
				string rootDirectory = Path.Combine( OutputDirectory, Path.GetFileName( GameSaveDirectory ) + "_" + GetReadableSaveFileName( GameSaveFilePath ) );
				manualSavesDirectory = Path.Combine( rootDirectory, "ManualSaves" );
				automatedSavesDirectory = Path.Combine( rootDirectory, "AutomatedSaves" );

				LoadSaveFiles( manualSavesDirectory, manualSaves, true );
				LoadSaveFiles( automatedSavesDirectory, automatedSaves, false );

				for( int i = automatedSaves.Count - 1; i >= 0; i-- )
					automatedSaveDirectoryNames.Add( Path.GetFileName( automatedSaves[i].directory ) );

				gameSaveFileDateTime = File.GetLastWriteTime( GameSaveFilePath );
			}

			manualSavesScrollView.UpdateList();
			automatedSavesScrollView.UpdateList();

			RefreshManualSaveNamesDropdown();

			if( manualSaves.Find( ( saveEntry ) => saveEntry.saveDate == gameSaveFileDateTime ) != null )
				manuallyLoadedGameDateTime = gameSaveFileDateTime;
		}

		private void LoadSaveFiles( string rootDirectory, DynamicCircularBuffer<SaveEntry> savesList, bool useDirectoryNameAsSaveName )
		{
			if( Directory.Exists( rootDirectory ) )
			{
				foreach( string saveDirectory in Directory.GetDirectories( rootDirectory ) )
				{
					string saveFile = GetSaveFilePath( saveDirectory );
					if( !string.IsNullOrEmpty( saveFile ) )
						savesList.Add( new SaveEntry( saveDirectory, File.GetLastWriteTime( saveFile ), useDirectoryNameAsSaveName ? Path.GetFileName( saveDirectory ) : null ) );
				}
			}

			// Simple bubble sort: https://stackoverflow.com/a/14768087/2373034
			for( int i = 0, length = savesList.Count; i < length; i++ )
			{
				for( int j = 0; j < length - 1; j++ )
				{
					if( savesList[j].saveDate > savesList[j + 1].saveDate )
					{
						SaveEntry temp = savesList[j + 1];
						savesList[j + 1] = savesList[j];
						savesList[j] = temp;
					}
				}
			}
		}

		private void SaveGameManual()
		{
			DateTime date = DateTime.Now;
			string saveDirectory = Path.Combine( manualSavesDirectory, ManualSaveName );

			SaveInternal( saveDirectory );

			// Manual save file's date should be set to current date so that it appears at the top
			string saveFile = GetSaveFilePath( saveDirectory );
			if( !string.IsNullOrEmpty( saveFile ) )
				File.SetLastWriteTime( saveFile, date );

			// Move the manual save to the end of the list (if exists)
			manualSaves.RemoveAll( ( saveEntry ) => saveEntry.saveName == ManualSaveName );
			manualSaves.Add( new SaveEntry( saveDirectory, date, ManualSaveName ) );

			manualSavesScrollView.UpdateList();

			RefreshManualSaveNamesDropdown();
		}

		private void SaveGameAutomated()
		{
			string saveDirectory;
			if( NumberOfAutomatedSaves <= automatedSaves.Count )
				saveDirectory = automatedSaves.RemoveFirst().directory;
			else
			{
				string directoryName = automatedSaves.Count.ToString();
				if( automatedSaveDirectoryNames.Contains( directoryName ) )
				{
					int directoryIndex = 0;
					while( automatedSaveDirectoryNames.Contains( directoryIndex.ToString() ) )
						directoryIndex++;

					saveDirectory = Path.Combine( automatedSavesDirectory, directoryIndex.ToString() );
				}
				else
					saveDirectory = Path.Combine( automatedSavesDirectory, directoryName );
			}

			SaveInternal( saveDirectory );

			automatedSaves.Add( new SaveEntry( saveDirectory, CurrentlyActiveSaveDateTime ) );
			automatedSaveDirectoryNames.Add( Path.GetFileName( saveDirectory ) );
			automatedSavesScrollView.UpdateList();
		}

		private void SaveInternal( string rootDirectory )
		{
			if( !string.IsNullOrEmpty( GameSaveFilePath ) && File.Exists( GameSaveFilePath ) )
				CopyDirectoryRecursively( GameSaveDirectory, rootDirectory );
		}

		private bool LoadGame( string rootDirectory )
		{
			if( !string.IsNullOrEmpty( GameSaveDirectory ) && !string.IsNullOrEmpty( GetSaveFilePath( rootDirectory ) ) && Directory.Exists( GameSaveDirectory ) )
			{
				CopyDirectoryRecursively( rootDirectory, GameSaveDirectory );
				return true;
			}

			return false;
		}

		private void OnSaveEntryClicked( SaveEntry saveEntry, bool isAutomatedSave )
		{
			loadConfirmationDialog.Show( saveEntry.saveName, () => // onLoad
			{
				if( LoadGame( saveEntry.directory ) && !isAutomatedSave )
					manuallyLoadedGameDateTime = File.GetLastWriteTime( GetSaveFilePath( saveEntry.directory ) );
			},
			() => // onEdit
			{
				saveEditorWindow.Show( saveEntry, GetSaveFilePath( saveEntry.directory ) );
			},
			() => // onDelete
			{
				Directory.Delete( saveEntry.directory, true );

				if( isAutomatedSave )
				{
					automatedSaves.Remove( saveEntry );
					automatedSavesScrollView.UpdateList();
				}
				else
				{
					manualSaves.Remove( saveEntry );
					manualSavesScrollView.UpdateList();
					RefreshManualSaveNamesDropdown();
				}
			} );
		}

		private void RefreshManualSaveNamesDropdown()
		{
			List<Dropdown.OptionData> manualSaveNames = new List<Dropdown.OptionData>( manualSaves.Count );
			if( manualSaves.Count == 0 )
				manualSaveNames.Add( new Dropdown.OptionData( !string.IsNullOrEmpty( ManualSaveName ) ? ManualSaveName : "My Save" ) );
			else
			{
				for( int i = 0; i < manualSaves.Count; i++ )
					manualSaveNames.Add( new Dropdown.OptionData( manualSaves[i].saveName ) );
			}

			manualSaveNamesDropdown.options = manualSaveNames;
		}

		private string[] GetAllSaveFilePaths( string saveDirectory )
		{
			List<string> result = new List<string>( 2 );
			if( !string.IsNullOrEmpty( saveDirectory ) )
			{
				string saveDirectoryRemote = Path.Combine( saveDirectory, "remote" );
				if( Directory.Exists( saveDirectoryRemote ) )
				{
					bool newSaveFilePathExplored = false;
					foreach( string saveDirectoryContent in Directory.GetFiles( saveDirectoryRemote, "*PSstate" ) )
					{
						if( saveDirectoryContent.EndsWith( "PSstate" ) )
						{
							result.Add( saveDirectoryContent );

							if( exploredGameSaveFilePaths.Add( saveDirectoryContent ) )
								newSaveFilePathExplored = true;
						}
					}

					if( newSaveFilePathExplored )
					{
						try
						{
							string[] _exploredGameSaveFilePaths = new string[exploredGameSaveFilePaths.Count];
							exploredGameSaveFilePaths.CopyTo( _exploredGameSaveFilePaths );

							PlayerPrefs.SetString( "CachedSaveFilePaths", JsonUtility.ToJson( new CachedSaveFilePaths() { paths = _exploredGameSaveFilePaths } ) );
							PlayerPrefs.Save();
						}
						catch( Exception e )
						{
							Debug.LogException( e );
						}
					}
				}
			}

			return result.ToArray();
		}

		private string GetSaveFilePath( string saveDirectory )
		{
			if( !string.IsNullOrEmpty( saveDirectory ) && !string.IsNullOrEmpty( GameSaveFilePath ) )
			{
				string saveDirectoryRemote = Path.Combine( saveDirectory, "remote" );
				if( Directory.Exists( saveDirectoryRemote ) )
				{
					string gameSaveFilename = Path.GetFileName( GameSaveFilePath );
					foreach( string saveDirectoryContent in Directory.GetFiles( saveDirectoryRemote, gameSaveFilename ) )
					{
						if( Path.GetFileName( saveDirectoryContent ) == gameSaveFilename )
							return saveDirectoryContent;
					}
				}
			}

			return "";
		}

		private string GetSteamSavesDirectory()
		{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
			for( int i = 0; i < 3; i++ )
			{
				string steamSaveDirectory = string.Format( @"{0}:\Program Files (x86)\Steam\userdata", (char) ( 'C' + i ) );
				if( Directory.Exists( steamSaveDirectory ) )
					return steamSaveDirectory;
			}
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
			string steamSaveDirectory = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.Personal ), "Library/Application Support/Steam/userdata" );
			if( Directory.Exists( steamSaveDirectory ) )
				return steamSaveDirectory;
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
			string steamSaveDirectory = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.Personal ), ".local/share/Steam/userdata" );
			if( Directory.Exists( steamSaveDirectory ) )
				return steamSaveDirectory;
#endif

			return "";
		}

		public static string GetReadableSaveFileName( string saveFilePath )
		{
			string filename = Path.GetFileName( saveFilePath );
			if( filename.StartsWith( "storePS" ) )
				return filename.Substring( "storePS".Length, filename.Length - "storePS".Length - "PSstate".Length );
			else
				return filename.Substring( 0, filename.Length - "PSstate".Length );
		}

		private void CopyDirectoryRecursively( string sourceDirectory, string destinationDirectory )
		{
			Directory.CreateDirectory( destinationDirectory );
			DirectoryInfo directory = new DirectoryInfo( sourceDirectory );

			foreach( FileInfo file in directory.GetFiles() )
			{
				string tempPath = Path.Combine( destinationDirectory, file.Name );
				file.CopyTo( tempPath, true );
			}

			foreach( DirectoryInfo subDirectory in directory.GetDirectories() )
			{
				string tempPath = Path.Combine( destinationDirectory, subDirectory.Name );
				CopyDirectoryRecursively( subDirectory.FullName, tempPath );
			}
		}
	}
}