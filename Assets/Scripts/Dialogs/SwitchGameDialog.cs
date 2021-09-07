using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class SwitchGameDialog : SaveFileSelectionDialog
	{
#pragma warning disable 0649
		[SerializeField]
		private Button cancelButton;
#pragma warning restore 0649

		protected override void Awake()
		{
			base.Awake();
			cancelButton.onClick.AddListener( () => gameObject.SetActive( false ) );
		}
	}
}