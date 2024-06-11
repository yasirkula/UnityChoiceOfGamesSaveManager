// #define RESET_REMOVED_ELEMENTS

namespace CoGSaveManager
{
	public class DynamicCircularBuffer<T>
	{
		private T[] arr;
		private int startIndex;

		public int Count { get; private set; }
		public T this[int index]
		{
			get { return arr[( startIndex + index ) % arr.Length]; }
			set { arr[( startIndex + index ) % arr.Length] = value; }
		}

		public DynamicCircularBuffer( int initialCapacity = 2 )
		{
			arr = new T[initialCapacity];
		}

		public void Add( T value )
		{
			if( Count >= arr.Length )
			{
				int prevSize = arr.Length;
				int newSize = prevSize > 0 ? prevSize * 2 : 2; // Size must be doubled (at least), or the shift operation below must consider IndexOutOfRange situations

				System.Array.Resize( ref arr, newSize );

				if( startIndex > 0 )
				{
					if( startIndex <= ( prevSize - 1 ) / 2 )
					{
						// Move elements [0,startIndex) to the end
						for( int i = 0; i < startIndex; i++ )
						{
							arr[i + prevSize] = arr[i];
#if RESET_REMOVED_ELEMENTS
							arr[i] = default( T );
#endif
						}
					}
					else
					{
						// Move elements [startIndex,prevSize) to the end
						int delta = newSize - prevSize;
						for( int i = prevSize - 1; i >= startIndex; i-- )
						{
							arr[i + delta] = arr[i];
#if RESET_REMOVED_ELEMENTS
							arr[i] = default( T );
#endif
						}

						startIndex += delta;
					}
				}
			}

			this[Count++] = value;
		}

		public bool Remove( T value )
		{
			for( int i = 0; i < Count; i++ )
			{
				if( this[i].Equals( value ) )
				{
					RemoveAt( i );
					return true;
				}
			}

			return false;
		}

		public void RemoveAt( int index )
		{
			for( ; index < Count - 1; index++ )
				this[index] = this[index + 1];

			Count--;
		}

		public int RemoveAll( System.Predicate<T> match )
		{
			int removedCount = 0;
			for( int i = Count - 1; i >= 0; i-- )
			{
				if( match( this[i] ) )
				{
					RemoveAt( i );
					removedCount++;
				}
			}

			return removedCount;
		}

		public T RemoveFirst()
		{
			T element = arr[startIndex];
#if RESET_REMOVED_ELEMENTS
			arr[startIndex] = default( T );
#endif

			if( ++startIndex >= arr.Length )
				startIndex = 0;

			Count--;
			return element;
		}

		public T RemoveLast()
		{
			T element = this[Count - 1];
#if RESET_REMOVED_ELEMENTS
			this[Count - 1] = default( T );
#endif

			Count--;
			return element;
		}

		public T Find( System.Predicate<T> match )
		{
			for( int i = 0; i < Count; i++ )
			{
				if( match( this[i] ) )
					return this[i];
			}

			return default( T );
		}

		public void Clear()
		{
			Count = 0;
			startIndex = 0;
		}
	}
}