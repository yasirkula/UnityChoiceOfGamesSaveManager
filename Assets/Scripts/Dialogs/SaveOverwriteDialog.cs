using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class SaveOverwriteDialog : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Text text;

		[SerializeField]
		private Button okButton, cancelButton;
#pragma warning restore 0649

		private string originalText;

		private System.Action onConfirm;

		private void Awake()
		{
			okButton.onClick.AddListener( () =>
			{
				if( onConfirm != null )
					onConfirm();

				gameObject.SetActive( false );
			} );

			cancelButton.onClick.AddListener( () => gameObject.SetActive( false ) );
		}

		public void Show( string saveName, System.Action onConfirm )
		{
			if( originalText == null )
				originalText = text.text;

			text.text = string.Format( originalText, saveName );

			this.onConfirm = onConfirm;

			gameObject.SetActive( true );
		}
	}
}