using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class ManualSaveNameDropdownEntry : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Dropdown dropdown;

		[SerializeField]
		private Toggle toggle;

		[SerializeField]
		private Button button;

		[SerializeField]
		private Text text;
#pragma warning restore 0649

		public static System.Action<string> OnSelectionChanged;

		private void Start()
		{
			// By default, Dropdown changes its selected toggle's background color. Revert this change
			if( toggle.targetGraphic )
				toggle.targetGraphic.color = toggle.colors.normalColor;

			button.onClick.AddListener( () =>
			{
				if( OnSelectionChanged != null )
					OnSelectionChanged( text.text );

				dropdown.Hide();
			} );
		}
	}
}