using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public delegate void OnSaveVisualClickedHandler( SaveEntryVisual saveEntryVisual );

	public class SaveEntryVisual : MonoBehaviour, IPointerClickHandler
	{
#pragma warning disable 0649
		[SerializeField]
		private Image background;

		[SerializeField]
		private Text text;

		[SerializeField]
		private Color activeBackgroundColor, activeTextColor;
		private Color inactiveBackgroundColor, inactiveTextColor;
#pragma warning restore 0649

		internal SavesScrollView listView;

		public int Position { get; set; }
		public SaveEntry SaveEntry { get; private set; }

		private void Awake()
		{
			inactiveBackgroundColor = background.color;
			inactiveTextColor = text.color;
		}

		public void SetContent( SaveEntry saveEntry )
		{
			SaveEntry = saveEntry;
			text.text = saveEntry.saveName;

			if( saveEntry.saveDate == SaveManager.CurrentlyActiveSaveDateTime )
			{
				background.color = activeBackgroundColor;
				text.color = activeTextColor;
			}
			else
			{
				background.color = inactiveBackgroundColor;
				text.color = inactiveTextColor;
			}
		}

		void IPointerClickHandler.OnPointerClick( PointerEventData eventData )
		{
			if( eventData.button == PointerEventData.InputButton.Left && listView.OnSaveVisualClicked != null )
				listView.OnSaveVisualClicked( this );
		}
	}
}