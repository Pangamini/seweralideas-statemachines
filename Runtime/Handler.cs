using System.Collections;
using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace SeweralIdeas.StateMachines
{
    public delegate void Handler<TReceiver>(TReceiver receiver);
    public delegate void Handler<TReceiver, TArg>(TReceiver receiver, TArg arg);
}