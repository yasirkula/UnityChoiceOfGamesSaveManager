using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class ReducedAutomatedSaveCountDialog : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Text text;

		[SerializeField]
		private Button okButton, cancelButton;
#pragma warning restore 0649

		private string originalText;

		private System.Action onConfirm, onCancel;

		private void Awake()
		{
			okButton.onClick.AddListener( () =>
			{
				if( onConfirm != null )
					onConfirm();

				gameObject.SetActive( false );
			} );

			cancelButton.onClick.AddListener( () =>
			{
				if( onCancel != null )
					onCancel();

				gameObject.SetActive( false );
			} );
		}

		public void Show( int newAutomatedSaveCount, int currentAutomatedSaveCount, System.Action onConfirm, System.Action onCancel )
		{
			if( originalText == null )
				originalText = text.text;

			text.text = string.Format( originalText, newAutomatedSaveCount, currentAutomatedSaveCount );

			this.onConfirm = onConfirm;
			this.onCancel = onCancel;

			gameObject.SetActive( true );
		}
	}
}