using SimpleFileBrowser;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Version = System.Version;

namespace CoGSaveManager
{
	[Serializable]
	public class CachedSaveFilePaths
	{
		public string[] paths;
	}

	[Serializable]
	public class RemoteData
	{
		public RemoteGameData[] RemoteGameData;
	}

	[Serializable]
	public class RemoteGameData
	{
		public string SaveFileName;
		public string ReadableName;
		public string GameID;
	}

	public class SaveManager : MonoBehaviour
	{
		public const string DEFAULT_PLAYTHROUGH_NAME = "Default";
		private const string PLAYTHROUGH_TIMESTAMP_FILE = "Timestamp";

		private const string MANUAL_SAVES_FOLDER = "ManualSaves";
		private const string AUTOMATED_SAVES_FOLDER = "AutomatedSaves";

		public const string STARRED_SAVE_FILE = "Starred";

		private const string SETTINGS_FILE = "Settings.json";

		private const string GAME_TITLE_FORMAT = "- {0} ({1}) -";

#pragma warning disable 0649
		[SerializeField]
		private float saveCheckInterval = 0.1f;
		private float nextAutomatedSaveCheckTime;

		[SerializeField]
		private Settings settings;

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
		private ProgressbarDialog progressbarDialog;

		[SerializeField]
		private NewVersionDialog newVersionDialog;

		[SerializeField]
		private CanvasScaler canvasScaler;

		[SerializeField]
		private RectTransform[] dropdownTemplates;

		[SerializeField]
		private Color gameSaveDirectoryInvalidColor;
		private Color gameSaveDirectoryValidColor;
		private Image gameSaveDirectoryBackground;
#pragma warning restore 0649

		private string m_gameSaveFilePath, gameSaveDirectory, gameSaveFilename, currentPlaythrough;
		private string GameSaveFilePath
		{
			get
			{
				if( m_gameSaveFilePath == null )
				{
					m_gameSaveFilePath = PlayerPrefs.GetString( "GameSaveFilePath", "" );
					if( !string.IsNullOrEmpty( m_gameSaveFilePath ) && !File.Exists( m_gameSaveFilePath ) )
						m_gameSaveFilePath = "";

					gameSaveFilename = string.IsNullOrEmpty( m_gameSaveFilePath ) ? "" : Path.GetFileName( m_gameSaveFilePath );
					currentPlaythrough = string.IsNullOrEmpty( m_gameSaveFilePath ) ? DEFAULT_PLAYTHROUGH_NAME : GetAllPlaythroughs( m_gameSaveFilePath )[0];

					RefreshGameTitle();
				}

				return m_gameSaveFilePath;
			}
			set
			{
				if( m_gameSaveFilePath != value )
				{
					if( !string.IsNullOrEmpty( value ) && !File.Exists( value ) )
						value = "";

					m_gameSaveFilePath = value;
					gameSaveFilename = !string.IsNullOrEmpty( value ) ? Path.GetFileName( value ) : "";
					gameSaveDirectory = !string.IsNullOrEmpty( value ) ? Path.GetDirectoryName( Path.GetDirectoryName( value ) ) : "";

					if( !string.IsNullOrEmpty( gameSaveDirectory ) )
					{
						gameSaveDirectoryInputField.text = gameSaveDirectory;
						gameSaveDirectoryBackground.color = gameSaveDirectoryValidColor;

						// Even if Steam isn't located at a pre-defined location, we can figure out its location by looking at GameSaveFilePath
						SteamSavesDirectory = new DirectoryInfo( gameSaveDirectory ).Parent.Parent.FullName;
					}
					else
					{
						// If we also set gameSaveDirectoryInputField.text to "" here, the input field's text will be cleared when user selects a folder with no valid save files inside it.
						// Instead, we want the input field's text to stay as is but rather change its color to gameSaveDirectoryInvalidColor
						gameSaveDirectoryBackground.color = gameSaveDirectoryInvalidColor;
					}

					if( string.IsNullOrEmpty( currentPlaythrough ) )
						currentPlaythrough = GetAllPlaythroughs( value )[0];

					RefreshGameTitle();

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

		private string m_steamSavesDirectory;
		private string SteamSavesDirectory
		{
			get
			{
				if( m_steamSavesDirectory == null )
				{
					m_steamSavesDirectory = PlayerPrefs.GetString( "SteamSavesPath", "" );
					if( !string.IsNullOrEmpty( m_steamSavesDirectory ) && !Directory.Exists( m_steamSavesDirectory ) )
						m_steamSavesDirectory = "";

					if( string.IsNullOrEmpty( m_steamSavesDirectory ) )
					{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
						for( int i = 0; i < 3; i++ )
						{
							string potentialDirectory = string.Format( @"{0}:\Program Files (x86)\Steam\userdata", (char) ( 'C' + i ) );
							if( Directory.Exists( potentialDirectory ) )
							{
								m_steamSavesDirectory = potentialDirectory;
								break;
							}
						}
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
						string potentialDirectory = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.Personal ), "Library/Application Support/Steam/userdata" );
						if( Directory.Exists( potentialDirectory ) )
							m_steamSavesDirectory = potentialDirectory;
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
						string potentialDirectory = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.Personal ), ".local/share/Steam/userdata" );
						if( Directory.Exists( potentialDirectory ) )
							m_steamSavesDirectory = potentialDirectory;
#endif
					}
				}

				return m_steamSavesDirectory;
			}
			set
			{
				if( m_steamSavesDirectory != value && !string.IsNullOrEmpty( value ) )
				{
					m_steamSavesDirectory = value;

					PlayerPrefs.SetString( "SteamSavesPath", value );
					PlayerPrefs.Save();
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
					manualSaveNameInputField.text = value;

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

		private string IgnoredNewVersion
		{
			get { return PlayerPrefs.GetString( "IgnoredVersion" ); }
			set
			{
				PlayerPrefs.SetString( "IgnoredVersion", value );
				PlayerPrefs.Save();
			}
		}

		private bool MigratedSaveFilesToV1_2_0
		{
			get { return PlayerPrefs.GetInt( "SaveMigration1_2_0", 0 ) == 1; }
			set
			{
				PlayerPrefs.SetInt( "SaveMigration1_2_0", value ? 1 : 0 );
				PlayerPrefs.Save();
			}
		}

		private readonly DynamicCircularBuffer<SaveEntry> manualSaves = new DynamicCircularBuffer<SaveEntry>( 64 );
		private readonly DynamicCircularBuffer<SaveEntry> automatedSaves = new DynamicCircularBuffer<SaveEntry>( 64 );
		private readonly HashSet<string> automatedSaveDirectoryNames = new HashSet<string>();

		private string[] invalidFilenameChars;
		private static readonly char[] streamReaderBuffer = new char[100];

		private DateTime manuallyLoadedGameDateTime;
		public static DateTime CurrentlyActiveSaveDateTime;

		private readonly HashSet<string> exploredGameSaveFilePaths = new HashSet<string>();

		private static readonly Dictionary<string, List<RemoteGameData>> remoteGameDataLookup = new Dictionary<string, List<RemoteGameData>>( 128 );

		private void Awake()
		{
			// Overwrite settings using local settings file (if exists)
			try
			{
				if( File.Exists( SETTINGS_FILE ) )
					JsonUtility.FromJsonOverwrite( File.ReadAllText( SETTINGS_FILE ), settings );
			}
			catch( Exception e )
			{
				Debug.LogException( e );
			}

			canvasScaler.GetComponent<Canvas>().pixelPerfect = settings.UIPixelPerfect;

			// Update UI resolution if needed
			if( !Mathf.Approximately( settings.UIResolution, canvasScaler.referenceResolution.y ) )
			{
				// Preserve dropdowns' distance to the screen's bottom edge so that when UI resolution increases, dropdowns can display more elements at once
				float[] dropdownBottomDistances = new float[dropdownTemplates.Length];
				for( int i = 0; i < dropdownTemplates.Length; i++ )
					dropdownBottomDistances[i] = dropdownTemplates[i].position.y - dropdownTemplates[i].sizeDelta.y * dropdownTemplates[i].lossyScale.y;

				// Update UI resolution
				canvasScaler.referenceResolution = new Vector2( canvasScaler.referenceResolution.x, Mathf.Max( 400f, settings.UIResolution ) );

				// CanvasScaler isn't updated unless its Update or OnEnable functions are called, trigger OnEnable immediately
				canvasScaler.enabled = false;
				canvasScaler.enabled = true;

				// Restore dropdowns' distance to the screen's bottom edge
				for( int i = 0; i < dropdownTemplates.Length; i++ )
					dropdownTemplates[i].sizeDelta = new Vector2( dropdownTemplates[i].sizeDelta.x, ( dropdownTemplates[i].position.y - dropdownBottomDistances[i] ) / dropdownTemplates[i].lossyScale.y );
			}

			try
			{
				StartCoroutine( CheckForUpdatesAsync() );
			}
			catch( Exception e )
			{
				Debug.LogException( e );
			}

			try
			{
				StartCoroutine( FetchRemoteGameDataAsync() );
			}
			catch( Exception e )
			{
				Debug.LogException( e );
			}

			gameSaveDirectoryBackground = gameSaveDirectoryInputField.GetComponent<Image>();
			gameSaveDirectoryValidColor = gameSaveDirectoryBackground.color;

			if( !string.IsNullOrEmpty( GameSaveFilePath ) && File.Exists( GameSaveFilePath ) )
				gameSaveDirectoryInputField.text = gameSaveDirectory = Path.GetDirectoryName( Path.GetDirectoryName( GameSaveFilePath ) );
			else
			{
				gameSaveDirectoryInputField.text = gameSaveDirectory = "";
				gameSaveDirectoryBackground.color = gameSaveDirectoryInvalidColor;
			}

			outputDirectoryInputField.text = OutputDirectory;
			manualSaveNameInputField.text = ManualSaveName;
			numberOfAutomatedSavesInputField.text = NumberOfAutomatedSaves.ToString();

			invalidFilenameChars = Array.ConvertAll( Path.GetInvalidFileNameChars(), ( ch ) => ch.ToString() );

			manualSaveNameInputField.onEndEdit.AddListener( ( value ) =>
			{
				// Strip save name of invalid filename characters
				value = RemoveInvalidFilenameCharsFromString( value );

				if( value.Length > 0 )
				{
					if( ManualSaveName != value )
						ManualSaveName = value;
					else // This is required because if value is stripped of invalid filename characters, we need to update the input field to reflect those changes
						manualSaveNameInputField.text = value;
				}
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
					initialPath = Path.GetDirectoryName( gameSaveDirectory );
				else
				{
					if( !string.IsNullOrEmpty( SteamSavesDirectory ) )
					{
						string[] steamUsers = Directory.GetDirectories( SteamSavesDirectory );
						initialPath = ( steamUsers.Length == 1 ) ? steamUsers[0] : SteamSavesDirectory;
					}
				}

				FileBrowser.ShowLoadDialog( ( paths ) =>
				{
					// There can be multiple save files in one directory (e.g. 'Heroes Rise' series stores all save files in the same directory)
					// In that case, we will prompt the user to manually select the desired save file that will be tracked for automated saves
					string[] gameSaveFilePaths = GetAllSaveFilePaths( paths[0] );
					gameSaveDirectoryInputField.text = paths[0];
					gameSaveDirectoryBackground.color = gameSaveFilePaths.Length > 0 ? gameSaveDirectoryValidColor : gameSaveDirectoryInvalidColor;

					if( gameSaveFilePaths.Length == 0 )
						GameSaveFilePath = "";
					else if( gameSaveFilePaths.Length == 1 )
					{
						currentPlaythrough = "";
						GameSaveFilePath = gameSaveFilePaths[0];
					}
					else
					{
						GameSaveFilePath = "";

						saveFileSelectionDialog.Show( gameSaveFilePaths, GameSaveFilePath, ( selectedSaveFilePath ) =>
						{
							currentPlaythrough = "";
							GameSaveFilePath = selectedSaveFilePath;
							LoadSaveFiles();
						}, false, settings.IgnoreArticlesWhileSortingGames );
					}

					LoadSaveFiles();
				}, null, FileBrowser.PickMode.Folders, initialPath: initialPath, title: "Select the target game's save directory" );
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

			switchGameDialog.playthroughsGetter = GetAllPlaythroughs;
			switchGameDialog.playthroughNameValidator = RemoveInvalidFilenameCharsFromString;

			gameTitleBackgroundButton.onClick.AddListener( () =>
			{
				if( exploredGameSaveFilePaths.Count > 0 )
				{
					string[] _exploredGameSaveFilePaths = new string[exploredGameSaveFilePaths.Count];
					exploredGameSaveFilePaths.CopyTo( _exploredGameSaveFilePaths );

					switchGameDialog.Show( _exploredGameSaveFilePaths, GameSaveFilePath, ( selectedSaveFilePath, selectedPlaythrough ) => // onConfirm
					{
						currentPlaythrough = !string.IsNullOrEmpty( selectedPlaythrough ) ? selectedPlaythrough : GetAllPlaythroughs( selectedSaveFilePath )[0];

						if( GameSaveFilePath != selectedSaveFilePath )
							GameSaveFilePath = selectedSaveFilePath;
						else if( !string.IsNullOrEmpty( selectedSaveFilePath ) ) // If GameSaveFilePath didn't change, we should still update gameTitleText with the new playthrough
							RefreshGameTitle();

						LoadSaveFiles();

						// If changing the value of GameSaveDirectory prompted saveFileSelectionDialog, close it because we've assigned GameSaveFilePath's value manually
						saveFileSelectionDialog.gameObject.SetActive( false );
					},
					( saveFile, playthrough ) => // onDeletePlaythrough
					{
						if( !string.IsNullOrEmpty( OutputDirectory ) && !string.IsNullOrEmpty( saveFile ) && File.Exists( saveFile ) )
						{
							string playthroughDirectory = Path.Combine( Path.Combine( OutputDirectory, GetSaveFileUserID( saveFile ) ), string.Concat( new FileInfo( saveFile ).Directory.Parent.Name, "_", GetReadableSaveFileName( saveFile, false ), "_", playthrough ) );
							if( Directory.Exists( playthroughDirectory ) )
								Directory.Delete( playthroughDirectory, true );

							// If currently active playthrough is deleted, refresh the save files immediately
							if( saveFile == GameSaveFilePath && playthrough == currentPlaythrough )
							{
								currentPlaythrough = GetAllPlaythroughs( GameSaveFilePath )[0];

								RefreshGameTitle();
								LoadSaveFiles();
							}
						}
					}, true, settings.IgnoreArticlesWhileSortingGames );
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
				if( !string.IsNullOrEmpty( SteamSavesDirectory ) )
				{
					foreach( string potentialSaveFilePath in Directory.GetFiles( SteamSavesDirectory, "*PSstate", SearchOption.AllDirectories ) )
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

			if( !MigratedSaveFilesToV1_2_0 )
				StartCoroutine( MigrateSaveFilesToV1_2_0() );

			nextAutomatedSaveCheckTime = Time.time + saveCheckInterval;
			Application.targetFrameRate = 60;
		}

		private void Update()
		{
#if UNITY_EDITOR
			// In Unity editor, stop play mode immediately if a script is compiling because otherwise it causes issues after compilation is finished (e.g. duplicate automated save issue)
			if( UnityEditor.EditorApplication.isCompiling )
			{
				Debug.Log( "<b>Exiting play mode because a script is compiling!</b>" );
				UnityEditor.EditorApplication.isPlaying = false;
				return;
			}
#endif

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
				string playthrough = string.IsNullOrEmpty( currentPlaythrough ) ? DEFAULT_PLAYTHROUGH_NAME : currentPlaythrough;
				string rootDirectory = Path.Combine( Path.Combine( OutputDirectory, GetSaveFileUserID( GameSaveFilePath ) ), string.Concat( Path.GetFileName( gameSaveDirectory ), "_", GetReadableSaveFileName( GameSaveFilePath, false ), "_", playthrough ) );
				manualSavesDirectory = Path.Combine( rootDirectory, MANUAL_SAVES_FOLDER );
				automatedSavesDirectory = Path.Combine( rootDirectory, AUTOMATED_SAVES_FOLDER );

				LoadSaveFiles( manualSavesDirectory, manualSaves, true );
				LoadSaveFiles( automatedSavesDirectory, automatedSaves, false );

				for( int i = automatedSaves.Count - 1; i >= 0; i-- )
					automatedSaveDirectoryNames.Add( Path.GetFileName( automatedSaves[i].directory ) );

				gameSaveFileDateTime = File.GetLastWriteTime( GameSaveFilePath );

				// Create/update the playthrough timestamp file
				Directory.CreateDirectory( rootDirectory );
				File.Create( Path.Combine( rootDirectory, PLAYTHROUGH_TIMESTAMP_FILE ) ).Close();
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
			{
				SaveEntry oldestSave = automatedSaves.RemoveFirst();
				oldestSave.IsStarred = false;

				saveDirectory = oldestSave.directory;
			}
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
				CopyDirectoryRecursively( gameSaveDirectory, rootDirectory, true );
		}

		private bool LoadGame( string rootDirectory )
		{
			if( !string.IsNullOrEmpty( GameSaveFilePath ) && File.Exists( GameSaveFilePath ) )
			{
				CopyDirectoryRecursively( rootDirectory, gameSaveDirectory, false );
				return true;
			}

			return false;
		}

		private void OnSaveEntryClicked( SaveEntry saveEntry, bool isAutomatedSave )
		{
			loadConfirmationDialog.Show( saveEntry.saveName, saveEntry.IsStarred, () => // onLoad
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
			},
			( isStarred ) => // onToggleStar
			{
				saveEntry.IsStarred = isStarred;

				if( isAutomatedSave )
					automatedSavesScrollView.UpdateList();
				else
					manualSavesScrollView.UpdateList();
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
					foreach( string saveDirectoryContent in Directory.GetFiles( saveDirectoryRemote, gameSaveFilename ) )
					{
						if( Path.GetFileName( saveDirectoryContent ) == gameSaveFilename )
							return saveDirectoryContent;
					}
				}
			}

			return "";
		}

		private string[] GetAllPlaythroughs( string saveFilePath )
		{
			if( !string.IsNullOrEmpty( OutputDirectory ) && !string.IsNullOrEmpty( saveFilePath ) && Directory.Exists( OutputDirectory ) && File.Exists( saveFilePath ) )
			{
				string rootDirectory = Path.Combine( OutputDirectory, GetSaveFileUserID( saveFilePath ) );
				if( Directory.Exists( rootDirectory ) )
				{
					string saveDirectoryPrefix = string.Concat( new FileInfo( saveFilePath ).Directory.Parent.Name, "_", GetReadableSaveFileName( saveFilePath, false ), "_" );

					string[] allPlaythroughs = Directory.GetDirectories( rootDirectory, saveDirectoryPrefix + "*", SearchOption.TopDirectoryOnly );
					if( allPlaythroughs.Length == 1 )
						return new string[1] { Path.GetFileName( allPlaythroughs[0] ).Substring( saveDirectoryPrefix.Length ) };
					else if( allPlaythroughs.Length > 1 )
					{
						DateTime[] playthroughTimestamps = new DateTime[allPlaythroughs.Length];
						for( int i = 0; i < allPlaythroughs.Length; i++ )
						{
							FileInfo timestampFile = new FileInfo( Path.Combine( allPlaythroughs[i], PLAYTHROUGH_TIMESTAMP_FILE ) );
							if( timestampFile.Exists )
								playthroughTimestamps[i] = timestampFile.LastWriteTime;
							else
								playthroughTimestamps[i] = new DateTime();
						}

						// Sort playthroughs using their timestamps in descending order (most recently modified playthrough comes first)
						Array.Sort( playthroughTimestamps, allPlaythroughs );
						Array.Reverse( allPlaythroughs );

						string[] result = new string[allPlaythroughs.Length];
						for( int i = 0; i < allPlaythroughs.Length; i++ )
							result[i] = Path.GetFileName( allPlaythroughs[i] ).Substring( saveDirectoryPrefix.Length );

						return result;
					}
				}
			}

			return new string[1] { DEFAULT_PLAYTHROUGH_NAME };
		}

		public static string GetReadableSaveFileName( string saveFilePath, bool humanReadable )
		{
			if( !humanReadable )
				return GetReadableSaveFileNameInternal( Path.GetFileName( saveFilePath ) );
			else
			{
				FileInfo saveFile = new FileInfo( saveFilePath );
				RemoteGameData gameData = GetRemoteGameData( saveFile );
				return ( gameData != null && !string.IsNullOrEmpty( gameData.ReadableName ) ) ? gameData.ReadableName : GetReadableSaveFileNameInternal( saveFile.Name );
			}
		}

		private static string GetReadableSaveFileNameInternal( string saveFileName )
		{
			if( saveFileName.StartsWith( "storePS" ) )
				return saveFileName.Substring( "storePS".Length, saveFileName.Length - "storePS".Length - "PSstate".Length );
			else
				return saveFileName.Substring( 0, saveFileName.Length - "PSstate".Length );
		}

		public static string GetSaveFileUserID( string saveFilePath )
		{
			return new FileInfo( saveFilePath ).Directory.Parent.Parent.Name;
		}

		private static RemoteGameData GetRemoteGameData( string saveFilePath )
		{
			return GetRemoteGameData( new FileInfo( saveFilePath ) );
		}

		private static RemoteGameData GetRemoteGameData( FileInfo saveFile )
		{
			string gameID = saveFile.Directory.Parent.Name;
			List<RemoteGameData> matchingGameData;
			if( remoteGameDataLookup.TryGetValue( gameID, out matchingGameData ) )
			{
				string saveFileName = saveFile.Name;
				for( int i = 0; i < matchingGameData.Count; i++ )
				{
					if( matchingGameData[i].SaveFileName == saveFileName )
						return matchingGameData[i];
				}
			}

			// Try fetching the game's name from the save file's contents ("choice_title")
			try
			{
				using( StreamReader streamReader = saveFile.OpenText() )
				{
					int count = streamReader.ReadBlock( streamReaderBuffer, 0, 100 );
					string saveContents = new string( streamReaderBuffer, 0, count );
					int gameTitleIndex = saveContents.IndexOf( "\"choice_title\":\"" );
					if( gameTitleIndex >= 0 )
					{
						gameTitleIndex += 16;
						int gameTitleEndIndex = saveContents.IndexOf( '"', gameTitleIndex );
						if( gameTitleEndIndex > gameTitleIndex )
						{
							RemoteGameData result = new RemoteGameData()
							{
								SaveFileName = GetReadableSaveFileName( saveFile.Name, false ),
								ReadableName = saveContents.Substring( gameTitleIndex, gameTitleEndIndex - gameTitleIndex ),
								GameID = gameID,
							};
#if UNITY_EDITOR
							// Log missing games' data which would make it easier for me to update the remote data: https://gist.github.com/yasirkula/27302fcc36117b4741ea4817c1569434
							Debug.LogWarning( JsonUtility.ToJson( result ) + "," );
#endif

							if( matchingGameData == null )
								remoteGameDataLookup[gameID] = matchingGameData = new List<RemoteGameData>( 2 );

							matchingGameData.Add( result );
							return result;
						}
					}
				}
			}
			catch( Exception e )
			{
				Debug.LogException( e );
			}

			return null;
		}

		private void RefreshGameTitle()
		{
			gameTitleText.text = string.IsNullOrEmpty( GameSaveFilePath ) ? "No Choice of Game selected" : string.Format( GAME_TITLE_FORMAT, GetReadableSaveFileName( GameSaveFilePath, true ), currentPlaythrough );
		}

		private IEnumerator CheckForUpdatesAsync()
		{
			UnityWebRequest webRequest = UnityWebRequest.Get( "https://api.github.com/repos/yasirkula/UnityChoiceOfGamesSaveManager/releases/latest" );
			yield return webRequest.Send();

			if( webRequest.isError )
				Debug.LogWarning( webRequest.error );
			else
			{
				const string VERSION_FIELD = "\"tag_name\"";

				string returnedJson = webRequest.downloadHandler.text;
				int versionNumberStartIndex = returnedJson.IndexOf( VERSION_FIELD );
				if( versionNumberStartIndex >= 0 )
				{
					// Read the up-to-date version from the returned JSON
					versionNumberStartIndex = returnedJson.IndexOf( '"', versionNumberStartIndex + VERSION_FIELD.Length );
					int versionNumberEndIndex = returnedJson.IndexOf( '"', versionNumberStartIndex + 1 );
					string upToDateVersion = returnedJson.Substring( versionNumberStartIndex + 1, versionNumberEndIndex - versionNumberStartIndex - 1 );
					if( upToDateVersion.StartsWith( "v", StringComparison.OrdinalIgnoreCase ) )
						upToDateVersion = upToDateVersion.Substring( 1 );

					// Check if this version is outdated
					if( upToDateVersion != IgnoredNewVersion && new Version( upToDateVersion ) > new Version( Application.version ) )
					{
						newVersionDialog.Show( upToDateVersion,
							() => Application.OpenURL( "https://github.com/yasirkula/UnityChoiceOfGamesSaveManager/releases" ),
							() => IgnoredNewVersion = upToDateVersion );
					}
				}
			}
		}

		private IEnumerator FetchRemoteGameDataAsync()
		{
			if( string.IsNullOrEmpty( settings.RemoteGameDataURL ) )
				yield break;

			// Load cached remote data (if exists) while fetching the up-to-date remote data from the server
			string cacheFilePath = Path.Combine( Application.temporaryCachePath, "CoGRemoteGameData.json" );
			if( File.Exists( cacheFilePath ) )
			{
				try
				{
					InitializeRemoteData( JsonUtility.FromJson<RemoteData>( File.ReadAllText( cacheFilePath ) ) );
				}
				catch { }
			}

			UnityWebRequest remoteGameDataFetch = UnityWebRequest.Get( settings.RemoteGameDataURL );
			yield return remoteGameDataFetch.Send();

			if( remoteGameDataFetch.isError )
				Debug.LogWarning( remoteGameDataFetch.error );
			else
			{
				try
				{
					string returnedJson = remoteGameDataFetch.downloadHandler.text;
					InitializeRemoteData( JsonUtility.FromJson<RemoteData>( returnedJson ) );

					// Cache the fetched data for the next session
					File.WriteAllText( cacheFilePath, returnedJson );

					// Refresh the title (its human-readable name may have changed)
					if( !string.IsNullOrEmpty( GameSaveFilePath ) && File.Exists( GameSaveFilePath ) )
						RefreshGameTitle();
				}
				catch( ArgumentException e ) // JSON parse errors, I don't think we should show the error popup for these
				{
					Debug.LogWarning( e.ToString() );
				}
			}
		}

		private void InitializeRemoteData( RemoteData data )
		{
			remoteGameDataLookup.Clear();

			if( data == null )
				return;

			if( data.RemoteGameData != null )
			{
				foreach( RemoteGameData remoteGameData in data.RemoteGameData )
				{
					if( !string.IsNullOrEmpty( remoteGameData.SaveFileName ) )
						remoteGameData.SaveFileName = string.Concat( "storePS", remoteGameData.SaveFileName, "PSstate" );

					List<RemoteGameData> matchingGameData;
					if( !remoteGameDataLookup.TryGetValue( remoteGameData.GameID, out matchingGameData ) )
						remoteGameDataLookup[remoteGameData.GameID] = matchingGameData = new List<RemoteGameData>( 2 );

					matchingGameData.Add( remoteGameData );
				}
			}
		}

		private void CopyDirectoryRecursively( string sourceDirectory, string destinationDirectory, bool isSaving )
		{
			Directory.CreateDirectory( destinationDirectory );
			DirectoryInfo directory = new DirectoryInfo( sourceDirectory );

			foreach( FileInfo file in directory.GetFiles() )
			{
				string filename = file.Name;
				if( !isSaving || filename == gameSaveFilename ) // While saving, copy only the 'storePS{GAME_NAME}PSstate' file (gameSaveFilename) which is the only needed file
					file.CopyTo( Path.Combine( destinationDirectory, filename ), true );
			}

			foreach( DirectoryInfo subDirectory in directory.GetDirectories() )
				CopyDirectoryRecursively( subDirectory.FullName, Path.Combine( destinationDirectory, subDirectory.Name ), isSaving );
		}

		private string RemoveInvalidFilenameCharsFromString( string str )
		{
			str = str.Trim();
			foreach( string invalidFilenameChar in invalidFilenameChars )
				str = str.Replace( invalidFilenameChar, "" );

			return str;
		}

		// In v1.2.0, save files have changes as follows compared to v1.0.0:
		// - Save files are grouped under Steam User IDs so that each Steam user's save files will be stored in a separate directory
		// - A game can have multiple playthroughs and each playthrough's save files will be stored in a separate directory
		//   (the default playthrough's name is DEFAULT_PLAYTHROUGH_NAME)
		// 
		// So, a legacy save file "565980_evertreeinn" will now be renamed as "{USER_ID}\565980_evertreeinn_{DEFAULT_PLAYTHROUGH_NAME}"
		// Legacy save files won't be deleted after migration so that even if something unexpected happens during migration, users won't lose their original (legacy) save files
		private IEnumerator MigrateSaveFilesToV1_2_0()
		{
			if( string.IsNullOrEmpty( OutputDirectory ) || !Directory.Exists( OutputDirectory ) )
				yield break;

			HashSet<string> legacySaveFiles = new HashSet<string>( Directory.GetDirectories( OutputDirectory ) );
			if( legacySaveFiles.Count == 0 )
				yield break;

			List<string> originalSaveFiles = new List<string>( 32 );
			List<string> saveFilesToMigrate = new List<string>( 32 );
			HashSet<string> allUserIDs = new HashSet<string>();

			foreach( string saveFilePath in exploredGameSaveFilePaths )
			{
				string legacySaveFile = Path.Combine( OutputDirectory, string.Concat( new FileInfo( saveFilePath ).Directory.Parent.Name, "_", GetReadableSaveFileName( saveFilePath, false ) ) );
				if( Directory.Exists( legacySaveFile ) )
				{
					originalSaveFiles.Add( saveFilePath );
					saveFilesToMigrate.Add( legacySaveFile );
					legacySaveFiles.Remove( legacySaveFile );
				}
			}

			progressbarDialog.Show( "Migrating saves from v1.0.0 to v1.2.0, please wait...", true );

			for( int i = 0; i < originalSaveFiles.Count; i++ )
			{
				string saveFileUserID = GetSaveFileUserID( originalSaveFiles[i] );
				allUserIDs.Add( saveFileUserID );

				CopyDirectoryRecursively( saveFilesToMigrate[i], Path.Combine( Path.Combine( OutputDirectory, saveFileUserID ), string.Concat( Path.GetFileName( saveFilesToMigrate[i] ), "_", DEFAULT_PLAYTHROUGH_NAME ) ), false );
				progressbarDialog.UpdateProgressbar( (float) ( i + 1 ) / ( originalSaveFiles.Count + legacySaveFiles.Count ), string.Concat( ( i + 1 ).ToString(), "/", ( originalSaveFiles.Count + legacySaveFiles.Count ).ToString() ) );

				yield return null;
			}

			// The remaining save files left in legacySaveFiles belong to the games that were uninstalled along with their Steam save files. These saves may belong to any user, so we should
			// copy them to all users' folders to be safe
			if( legacySaveFiles.Count > 0 )
			{
				string[] _allUserIDs = new string[allUserIDs.Count];
				allUserIDs.CopyTo( _allUserIDs );

				int progressbarValue = originalSaveFiles.Count;
				foreach( string legacySaveFile in legacySaveFiles )
				{
					if( !Directory.Exists( Path.Combine( legacySaveFile, MANUAL_SAVES_FOLDER ) ) && !Directory.Exists( Path.Combine( legacySaveFile, AUTOMATED_SAVES_FOLDER ) ) )
						continue;

					foreach( string userID in _allUserIDs )
					{
						CopyDirectoryRecursively( legacySaveFile, Path.Combine( Path.Combine( OutputDirectory, userID ), string.Concat( Path.GetFileName( legacySaveFile ), "_", DEFAULT_PLAYTHROUGH_NAME ) ), false );
						yield return null;
					}

					progressbarValue++;
					progressbarDialog.UpdateProgressbar( (float) progressbarValue / ( originalSaveFiles.Count + legacySaveFiles.Count ), string.Concat( progressbarValue.ToString(), "/", ( originalSaveFiles.Count + legacySaveFiles.Count ).ToString() ) );
				}
			}

			yield return new WaitForSeconds( 0.5f );

			progressbarDialog.gameObject.SetActive( false );

			MigratedSaveFilesToV1_2_0 = true;
			LoadSaveFiles();
		}
	}
}