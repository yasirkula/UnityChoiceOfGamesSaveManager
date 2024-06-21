using System;
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
		private Button loadButton, editButton, choicesButton, deleteButton, cancelButton, toggleStarButton;
		private Image toggleStarButtonImage;

		[SerializeField]
		private Sprite toggleStarButtonOffIcon, toggleStarButtonOnIcon;
#pragma warning restore 0649

		private Action onLoad, onEdit, onSeeChoices, onDelete;
		private Action<bool> onToggleStar;

		private bool isStarred;

		private void Awake()
		{
			toggleStarButtonImage = toggleStarButton.GetComponent<Image>();

			loadButton.onClick.AddListener( () => InvokeCallbackAndHide( onLoad ) );
			editButton.onClick.AddListener( () => InvokeCallbackAndHide( onEdit ) );
			choicesButton.onClick.AddListener( () => InvokeCallbackAndHide( onSeeChoices ) );
			deleteButton.onClick.AddListener( () => InvokeCallbackAndHide( onDelete ) );
			cancelButton.onClick.AddListener( () => gameObject.SetActive( false ) );

			toggleStarButton.onClick.AddListener( () =>
			{
				SetStarred( !isStarred );

				if( onToggleStar != null )
					onToggleStar( isStarred );
			} );
		}

		public void Show( string saveName, bool isStarred, Action onLoad, Action onEdit, Action onSeeChoices, Action onDelete, Action<bool> onToggleStar )
		{
			text.text = saveName;

			this.onLoad = onLoad;
			this.onEdit = onEdit;
			this.onSeeChoices = onSeeChoices;
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

		private void InvokeCallbackAndHide( Action callback )
		{
			if( callback != null )
				callback();

			gameObject.SetActive( false );
		}
	}
}