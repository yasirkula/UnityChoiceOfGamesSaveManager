using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class SaveFileSelectionDialog : MonoBehaviour
	{
		private class GameNameComparer : IComparer<string>
		{
			private readonly bool ignoreArticlesWhileSorting;

			public GameNameComparer( bool ignoreArticlesWhileSorting )
			{
				this.ignoreArticlesWhileSorting = ignoreArticlesWhileSorting;
			}

			public int Compare( string x, string y )
			{
				if( ignoreArticlesWhileSorting )
				{
					if( x.StartsWith( "The ", StringComparison.OrdinalIgnoreCase ) )
						x = x.Substring( 4 );
					if( x.StartsWith( "A ", StringComparison.OrdinalIgnoreCase ) )
						x = x.Substring( 2 );
					if( x.StartsWith( "An ", StringComparison.OrdinalIgnoreCase ) )
						x = x.Substring( 3 );

					if( y.StartsWith( "The ", StringComparison.OrdinalIgnoreCase ) )
						y = y.Substring( 4 );
					if( y.StartsWith( "A ", StringComparison.OrdinalIgnoreCase ) )
						y = y.Substring( 2 );
					if( y.StartsWith( "An ", StringComparison.OrdinalIgnoreCase ) )
						y = y.Substring( 3 );
				}

				return x.CompareTo( y );
			}
		}

#pragma warning disable 0649
		[SerializeField]
		protected Dropdown dropdown;

		[SerializeField]
		private Button okButton;
#pragma warning restore 0649

		private string[] saveFiles;
		private Action<string> onConfirm;

		protected virtual void Awake()
		{
			okButton.onClick.AddListener( () =>
			{
				if( onConfirm != null )
					onConfirm( saveFiles[dropdown.value] );

				gameObject.SetActive( false );
			} );
		}

		public void Show( string[] saveFiles, string currentSaveFile, Action<string> onConfirm, bool showUserIDs, bool ignoreArticlesWhileSorting )
		{
			this.saveFiles = saveFiles;
			this.onConfirm = onConfirm;

			string[] userIDs = showUserIDs ? new string[saveFiles.Length] : null;
			if( showUserIDs )
			{
				string prevUserID = null;
				bool allUserIDsSame = true;
				for( int i = 0; i < saveFiles.Length; i++ )
				{
					userIDs[i] = SaveManager.GetSaveFileUserID( saveFiles[i] );
					if( allUserIDsSame && !string.IsNullOrEmpty( prevUserID ) && prevUserID != userIDs[i] )
						allUserIDsSame = false;
					else
						prevUserID = userIDs[i];
				}

				// Don't show Steam User IDs if all saves belong to the same person (for clarity)
				if( allUserIDsSame )
					showUserIDs = false;
			}

			string[] saveFileNames = new string[saveFiles.Length];
			for( int i = 0; i < saveFiles.Length; i++ )
				saveFileNames[i] = SaveManager.GetReadableSaveFileName( saveFiles[i], true );

			// Sort save files by their readable names
			Array.Sort( saveFileNames, saveFiles, new GameNameComparer( ignoreArticlesWhileSorting ) );

			List<Dropdown.OptionData> _saveFiles = new List<Dropdown.OptionData>( saveFiles.Length );
			for( int i = 0; i < saveFiles.Length; i++ )
				_saveFiles.Add( new Dropdown.OptionData( !showUserIDs ? saveFileNames[i] : string.Concat( userIDs[i], "/", saveFileNames[i] ) ) );

			dropdown.options = _saveFiles;
			dropdown.value = Mathf.Max( 0, Array.IndexOf( saveFiles, currentSaveFile ) );

			gameObject.SetActive( true );
		}
	}
}