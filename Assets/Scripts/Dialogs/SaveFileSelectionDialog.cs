using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class SaveFileSelectionDialog : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		protected Dropdown dropdown;

		[SerializeField]
		private Button okButton;
#pragma warning restore 0649

		private string[] saveFiles;
		private System.Action<string> onConfirm;

		protected virtual void Awake()
		{
			okButton.onClick.AddListener( () =>
			{
				if( onConfirm != null )
					onConfirm( saveFiles[dropdown.value] );

				gameObject.SetActive( false );
			} );
		}

		public void Show( string[] saveFiles, string currentSaveFile, System.Action<string> onConfirm, bool showUserIDs )
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

			List<Dropdown.OptionData> _saveFiles = new List<Dropdown.OptionData>( saveFiles.Length );
			for( int i = 0; i < saveFiles.Length; i++ )
			{
				string saveFileName = SaveManager.GetReadableSaveFileName( saveFiles[i] );
				_saveFiles.Add( new Dropdown.OptionData( !showUserIDs ? saveFileName : string.Concat( userIDs[i], "/", saveFileName ) ) );
			}

			dropdown.options = _saveFiles;
			dropdown.value = Mathf.Max( 0, System.Array.IndexOf( saveFiles, currentSaveFile ) );

			gameObject.SetActive( true );
		}
	}
}