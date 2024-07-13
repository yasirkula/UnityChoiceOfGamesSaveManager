using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public static class ExtensionFunctions
	{
		public static bool ContainsAt( this string str, string value, int index )
		{
			if( index + value.Length > str.Length )
				return false;

			for( int i = 0; i < value.Length; i++ )
			{
				if( str[index + i] != value[i] )
					return false;
			}

			return true;
		}

		public static void SetOpacity( this Graphic graphic, float opacity )
		{
			Color color = graphic.color;
			color.a = opacity;
			graphic.color = color;
		}

		public static Vector2 ClampScrollPosition( this ScrollRect scrollView, Vector2 scrollPosition )
		{
			Vector2 scrollableSize = scrollView.content.rect.size - scrollView.viewport.rect.size;
			scrollPosition.x = Mathf.Min( 0f, Mathf.Max( scrollPosition.x, -scrollableSize.x ) );
			scrollPosition.y = Mathf.Max( 0f, Mathf.Min( scrollPosition.y, scrollableSize.y ) );
			return scrollPosition;
		}
	}
}