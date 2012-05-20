using System;
using System.Collections;
using System.Threading;

namespace WinMD5
{
	/** A Thread safe wrapper
	 * for the Queue class.
	 */
	public class MTQueue
	{
		protected Queue q;

		public MTQueue()
		{
			q=new Queue();
		}

		public object Get()
		{
			lock (q)
			{
				while (true)
				{
					if (q.Count>0)
						return q.Dequeue();

					Monitor.Wait(q);
				}
			}
		}
    
		/*
		 * This would be a handy method...
		public void PutFront(object o)
		{
			lock (q)
			{
				q.Enqueue(o);
				Monitor.Pulse(q);
			}
		}
*/

		public void Put(object o)
		{
			lock (q)
			{
				q.Enqueue(o);
				Monitor.Pulse(q);
			}
		}

		public int Count()
		{
			lock (q)
			{
				return q.Count;
			}
		}

		public void Clear()
		{
			lock (q)
			{
				q.Clear();
			}
		}
	}
}
