using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class PlaythroughDropdownEntry : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Dropdown dropdown;

		[SerializeField]
		private Toggle toggle;

		[SerializeField]
		private Button okButton, deleteButton;

		[SerializeField]
		private Text text, deleteButtonText;
#pragma warning restore 0649

		private int deleteButtonClickCount = 0;

		public static System.Action<string> OnSelectionChanged;
		public static System.Action<int> OnPlaythroughDeleted;

		public static bool CanDeletePlaythroughs;

		private void Start()
		{
			// By default, Dropdown changes its selected toggle's background color. Revert this change
			if( toggle.targetGraphic )
				toggle.targetGraphic.color = toggle.colors.normalColor;

			okButton.onClick.AddListener( () =>
			{
				if( OnSelectionChanged != null )
					OnSelectionChanged( text.text );

				dropdown.Hide();
			} );

			if( !CanDeletePlaythroughs )
				deleteButton.gameObject.SetActive( false );
			else
			{
				// Delete the playthrough if Delete button is clicked 3 times (at this point, we can be fairly certain that it wasn't accidental)
				deleteButton.onClick.AddListener( () =>
				{
					switch( ++deleteButtonClickCount )
					{
						case 1: deleteButtonText.text = "Delete?"; break;
						case 2: deleteButtonText.text = "Delete??"; break;
						case 3:
						{
							if( OnPlaythroughDeleted != null )
								OnPlaythroughDeleted( transform.GetSiblingIndex() - 1 ); // Sibling at index 0 is the template item which is always inactive

							dropdown.Hide();
							break;
						}
					}
				} );
			}
		}
	}
}