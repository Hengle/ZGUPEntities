﻿using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace ZG
{
    public class ScrollRectComponentEx : ScrollRectComponent
    {
        public ActiveEvent onPreviousChanged;
        public ActiveEvent onNextChanged;

        public ScrollRectToggle toggleStyle;

        private bool __isMoving;
        private int2 __selectedIndex = IndexNull;
        private ScrollRectToggle[] __toggles;

        private static readonly int2 IndexNull = new int2(-1, -1);
        
        public override int2 count
        {
            get
            {
                int version = base.version;
                var result = base.count;
                if (version == base.version || toggleStyle == null)
                    return result;

                int count = math.max(result.x, result.y);
                if (count > 0)
                {
                    int i, length;
                    ScrollRectToggle toggle;
                    if (__toggles == null)
                    {
                        length = 0;

                        __toggles = new ScrollRectToggle[count];
                    }
                    else
                    {
                        length = __toggles.Length;
                        for (i = count; i < length; ++i)
                        {
                            toggle = __toggles[i];
                            if (toggle != null)
                                Destroy(toggle.gameObject);
                        }

                        Array.Resize(ref __toggles, count);
                    }

                    GameObject gameObject;
                    Transform parent = toggleStyle == null ? null : toggleStyle.transform;
                    parent = parent == null ? null : parent.parent;
                    for (i = length; i < count; ++i)
                    {
                        toggle = Instantiate(toggleStyle, parent);
                        if (toggle == null)
                            continue;

                        toggle.handler = this;
                        toggle.index = i;

                        gameObject = toggle.gameObject;
                        if (gameObject != null)
                            gameObject.SetActive(true);

                        __toggles[i] = toggle;
                    }

                    int2 temp = selectedIndex;
                    int min = 0, max = count - 1, index = math.clamp(math.max(temp.x, temp.y), min, max);
                    toggle = __toggles[index];
                    if (toggle != null && toggle.onSelected != null)
                    {
                        __isMoving = true;
                        toggle.onSelected.Invoke();
                        __isMoving = false;
                    }

                    if (onPreviousChanged != null)
                        onPreviousChanged.Invoke(index > min);

                    if (onNextChanged != null)
                        onNextChanged.Invoke(index < max);
                }
                else
                {
                    if (__toggles != null)
                    {
                        foreach (ScrollRectToggle toogle in __toggles)
                        {
                            if (toogle != null)
                                Destroy(toogle.gameObject);
                        }

                        __toggles = null;
                    }

                    if (onPreviousChanged != null)
                        onPreviousChanged.Invoke(false);

                    if (onNextChanged != null)
                        onNextChanged.Invoke(false);
                }

                return result;
            }
        }

        public int2 selectedIndex
        {
            get
            {
                return math.all(__selectedIndex == IndexNull) ? index : math.min(__selectedIndex, __toggles == null ? 0 : __toggles.Length - 1);
            }
        }

        public int axis
        {
            get
            {
                ScrollRect scrollRect = base.scrollRect;
                int axis = scrollRect != null && scrollRect.horizontal ? 0 : 1;
                return axis;
            }
        }

        public IReadOnlyList<ScrollRectToggle> toggles
        {
            get
            {
                return __toggles;
            }
        }

        public ScrollRectComponentEx()
        {
            onChanged += __OnChanged;
        }

        public ScrollRectToggle Get(int index)
        {
            return index < 0 || index >= (__toggles == null ? 0 : __toggles.Length) ? null : __toggles[index];
        }

        public void Next()
        {
            Move(1);
        }

        public void Previous()
        {
            Move(-1);
        }

        public void SetTo(int index) => MoveTo(index);

        public void Move(int offset)
        {
            int2 index = selectedIndex;
            index[axis] += offset;

            MoveTo(index);
        }

        public void MoveTo(int index)
        {
            int2 result = int2.zero;
            result[axis] = index;

            MoveTo(result);
        }

        public override void MoveTo(in int2 destination)
        {
            if (__isMoving)
                return;

            int2 source = selectedIndex;
            /*if (math.all(source == destination))
                return false;*/
            
            __Update(math.max(source.x, source.y), math.max(destination.x, destination.y));

            __selectedIndex = destination;
            
            base.MoveTo(destination);
        }

        public override void UpdateData()
        {
            __selectedIndex = IndexNull;

            base.UpdateData();
        }

        private void __OnChanged(float2 index)
        {
            __OnChanged((int2)math.round(index));
        }

        private void __OnChanged(int2 source)
        {
            bool isNull = math.all(__selectedIndex == IndexNull);
            if (!isNull && !math.all(math.min(__selectedIndex, __toggles == null ? 0 : __toggles.Length - 1) == source))
                return;

            int length = __toggles == null ? 0 : __toggles.Length, index = math.clamp(math.max(source.x, source.y), 0, length - 1);
            ScrollRectToggle toggle = length > 0 ? __toggles[index] : null;
            if (toggle != null && toggle.onSelected != null)
            {
                __isMoving = true;
                toggle.onSelected.Invoke();
                __isMoving = false;
            }

            if (isNull)
            {
                int2 destination = base.index;
                __Update(math.max(destination.x, destination.y), index);
            }
            else
                __selectedIndex = IndexNull;
        }

        private void __Update(int source, int destination)
        {
            int length = __toggles == null ? 0 : __toggles.Length, min = 0, max = length - 1;
            if (destination <= min)
            {
                if (onPreviousChanged != null)
                    onPreviousChanged.Invoke(false);
            }
            else if (source <= min)
            {
                if (onPreviousChanged != null)
                    onPreviousChanged.Invoke(true);
            }

            if (destination >= max)
            {
                if (onNextChanged != null)
                    onNextChanged.Invoke(false);
            }
            else if (source >= max)
            {
                if (onNextChanged != null)
                    onNextChanged.Invoke(true);
            }
        }
    }
}