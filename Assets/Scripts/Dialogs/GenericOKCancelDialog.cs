using System;
using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class GenericOKCancelDialog : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Text text;

		[SerializeField]
		private Button okButton, cancelButton;
#pragma warning restore 0649

		private Action onConfirm, onCancel;

		private void Awake()
		{
			okButton.onClick.AddListener( () =>
			{
				gameObject.SetActive( false );

				if( onConfirm != null )
					onConfirm();
			} );

			cancelButton.onClick.AddListener( () =>
			{
				gameObject.SetActive( false );

				if( onCancel != null )
					onCancel();
			} );
		}

		public void Show( string text, Action onConfirm, Action onCancel = null )
		{
			this.text.text = text;
			this.onConfirm = onConfirm;
			this.onCancel = onCancel;

			gameObject.SetActive( true );
		}
	}
}