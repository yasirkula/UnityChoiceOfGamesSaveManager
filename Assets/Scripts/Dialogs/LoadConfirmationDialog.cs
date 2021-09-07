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
		private Button loadButton, deleteButton, cancelButton;
#pragma warning restore 0649

		private System.Action onLoad, onDelete;

		private void Awake()
		{
			loadButton.onClick.AddListener( () =>
			{
				if( onLoad != null )
					onLoad();

				gameObject.SetActive( false );
			} );

			deleteButton.onClick.AddListener( () =>
			{
				if( onDelete != null )
					onDelete();

				gameObject.SetActive( false );
			} );

			cancelButton.onClick.AddListener( () => gameObject.SetActive( false ) );
		}

		public void Show( string saveName, System.Action onLoad, System.Action onDelete )
		{
			text.text = saveName;

			this.onLoad = onLoad;
			this.onDelete = onDelete;

			gameObject.SetActive( true );
		}
	}
}