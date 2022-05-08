using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class LoadConfirmationDialog : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Text text;

		[SerializeField]
		private Button loadButton, editButton, deleteButton, cancelButton, toggleStarButton;
		private Image toggleStarButtonImage;

		[SerializeField]
		private Sprite toggleStarButtonOffIcon, toggleStarButtonOnIcon;
#pragma warning restore 0649

		private System.Action onLoad, onEdit, onDelete;
		private System.Action<bool> onToggleStar;

		private bool isStarred;

		private void Awake()
		{
			toggleStarButtonImage = toggleStarButton.GetComponent<Image>();

			loadButton.onClick.AddListener( () =>
			{
				if( onLoad != null )
					onLoad();

				gameObject.SetActive( false );
			} );

			editButton.onClick.AddListener( () =>
			{
				if( onEdit != null )
					onEdit();

				gameObject.SetActive( false );
			} );

			deleteButton.onClick.AddListener( () =>
			{
				if( onDelete != null )
					onDelete();

				gameObject.SetActive( false );
			} );

			cancelButton.onClick.AddListener( () => gameObject.SetActive( false ) );

			toggleStarButton.onClick.AddListener( () =>
			{
				SetStarred( !isStarred );

				if( onToggleStar != null )
					onToggleStar( isStarred );
			} );
		}

		public void Show( string saveName, bool isStarred, System.Action onLoad, System.Action onEdit, System.Action onDelete, System.Action<bool> onToggleStar )
		{
			text.text = saveName;

			this.onLoad = onLoad;
			this.onEdit = onEdit;
			this.onDelete = onDelete;
			this.onToggleStar = onToggleStar;

			gameObject.SetActive( true );

			SetStarred( isStarred );
		}

		private void SetStarred( bool isStarred )
		{
			this.isStarred = isStarred;
			toggleStarButtonImage.sprite = isStarred ? toggleStarButtonOnIcon : toggleStarButtonOffIcon;
		}
	}
}