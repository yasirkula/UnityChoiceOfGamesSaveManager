using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class NewVersionDialog : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Text text;

		[SerializeField]
		private Button closeButton, ignoreButton, downloadButton;
#pragma warning restore 0649

		private string originalText;

		private System.Action onDownload, onIgnore;

		private void Awake()
		{
			downloadButton.onClick.AddListener( () =>
			{
				if( onDownload != null )
					onDownload();

				gameObject.SetActive( false );
			} );

			ignoreButton.onClick.AddListener( () =>
			{
				if( onIgnore != null )
					onIgnore();

				gameObject.SetActive( false );
			} );

			closeButton.onClick.AddListener( () => gameObject.SetActive( false ) );
		}

		public void Show( string newVersion, System.Action onDownload, System.Action onIgnore )
		{
			if( originalText == null )
				originalText = text.text;

			text.text = string.Format( originalText, newVersion );

			this.onDownload = onDownload;
			this.onIgnore = onIgnore;

			gameObject.SetActive( true );
		}
	}
}