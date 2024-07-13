using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace CoGSaveManager
{
	public class PointerEventListener : MonoBehaviour, IPointerClickHandler
	{
		public UnityAction<PointerEventData> OnClick;

		void IPointerClickHandler.OnPointerClick( PointerEventData eventData )
		{
			if( OnClick != null )
				OnClick( eventData );
		}
	}
}