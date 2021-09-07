using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class SaveFileSelectionDialog : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Dropdown dropdown;

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

		public void Show( string[] saveFiles, string currentSaveFile, System.Action<string> onConfirm )
		{
			this.saveFiles = saveFiles;
			this.onConfirm = onConfirm;

			List<Dropdown.OptionData> _saveFiles = new List<Dropdown.OptionData>( saveFiles.Length );
			for( int i = 0; i < saveFiles.Length; i++ )
				_saveFiles.Add( new Dropdown.OptionData( SaveManager.GetReadableSaveFileName( saveFiles[i] ) ) );

			dropdown.options = _saveFiles;
			dropdown.value = Mathf.Max( 0, System.Array.IndexOf( saveFiles, currentSaveFile ) );

			gameObject.SetActive( true );
		}
	}
}