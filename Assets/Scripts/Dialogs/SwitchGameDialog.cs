using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class SwitchGameDialog : SaveFileSelectionDialog
	{
#pragma warning disable 0649
		[SerializeField]
		private Button cancelButton;

		[SerializeField]
		private InputField playthroughInputField;

		[SerializeField]
		private Dropdown playthroughsDropdown;
#pragma warning restore 0649

		private string currentSaveFile;

		private System.Action<string, string> onDeletePlaythrough;

		internal System.Func<string, string[]> playthroughsGetter;
		internal System.Func<string, string> playthroughNameValidator;

		protected override void Awake()
		{
			base.Awake();

			playthroughInputField.onEndEdit.AddListener( ( value ) => playthroughInputField.text = playthroughNameValidator( value ) );

			PlaythroughDropdownEntry.OnSelectionChanged = ( value ) => playthroughInputField.text = value;
			PlaythroughDropdownEntry.OnPlaythroughDeleted = ( index ) =>
			{
				List<Dropdown.OptionData> playthroughs = playthroughsDropdown.options;
				bool shouldUpdateInputField = ( playthroughInputField.text == playthroughs[index].text );
				int newDropdownValue = Mathf.Clamp( ( index > playthroughs.Count ) ? ( index - 1 ) : index, 0, Mathf.Max( 0, playthroughs.Count - 2 ) );

				if( onDeletePlaythrough != null )
					onDeletePlaythrough( currentSaveFile, playthroughs[index].text );

				playthroughs.RemoveAt( index );
				if( playthroughs.Count == 0 )
					playthroughs.Add( new Dropdown.OptionData( SaveManager.DEFAULT_PLAYTHROUGH_NAME ) );

				playthroughsDropdown.options = playthroughs;
				playthroughsDropdown.value = newDropdownValue;

				if( shouldUpdateInputField )
					playthroughInputField.text = playthroughs[newDropdownValue].text;

				PlaythroughDropdownEntry.CanDeletePlaythroughs = ( playthroughs.Count > 1 );
			};

			cancelButton.onClick.AddListener( () => gameObject.SetActive( false ) );
		}

		public void Show( string[] saveFiles, string currentSaveFile, System.Action<string, string> onConfirm, System.Action<string, string> onDeletePlaythrough, bool showUserIDs )
		{
			this.onDeletePlaythrough = onDeletePlaythrough;

			dropdown.onValueChanged.RemoveAllListeners();

			Show( saveFiles, currentSaveFile, ( selectedSaveFilePath ) =>
			{
				if( onConfirm != null )
					onConfirm( selectedSaveFilePath, playthroughNameValidator( playthroughInputField.text ) );
			}, showUserIDs );

			dropdown.onValueChanged.AddListener( ( value ) => RefreshPlaythroughs( saveFiles[value] ) );

			RefreshPlaythroughs( currentSaveFile );
		}

		private void RefreshPlaythroughs( string saveFile )
		{
			currentSaveFile = saveFile;

			string[] playthroughs = playthroughsGetter( saveFile );
			List<Dropdown.OptionData> _playthroughs = new List<Dropdown.OptionData>( playthroughs.Length );
			for( int i = 0; i < playthroughs.Length; i++ )
				_playthroughs.Add( new Dropdown.OptionData( playthroughs[i] ) );

			playthroughsDropdown.options = _playthroughs;
			playthroughsDropdown.value = 0;
			playthroughInputField.text = playthroughs[0];

			PlaythroughDropdownEntry.CanDeletePlaythroughs = ( playthroughs.Length > 1 );
		}
	}
}