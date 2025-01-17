﻿using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
//using UnityEngine.SceneManagement;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace ZG
{
    public enum GameObjectEntityStatus
    {
        None,
        Deserializing,
        Creating,
        Created,
        Destroied,
        Invalid
    }

    public interface IGameObjectEntity
    {
        GameObjectEntityStatus status { get; }

        Entity entity { get; }

        World world { get; }
    }

    public struct GameObjectEntityWrapper : IGameObjectEntity
    {
        public GameObjectEntityStatus status { get; }
        public Entity entity { get; }

        public World world { get; }

        public GameObjectEntityWrapper(World world, in Entity entity, GameObjectEntityStatus status = GameObjectEntityStatus.Created)
        {
            this.status = status;
            this.entity = entity;
            this.world = world;
        }
    }

    public struct GameObjectEntityHandle : IBufferElementData
    {
        public GCHandle value; 
    }

    public class GameObjectEntity : GameObjectEntityDefinition, IGameObjectEntity, ISerializationCallbackReceiver
    {
        public enum DeserializedType
        {
            Normal,
            IgnoreSceneLoading,
            InstanceOnly
        }

        internal class DestroiedEntity : IDisposable
        {
            public int instanceID;
            public GameObjectEntityStatus status;
            public Entity entity;
            public GameObjectEntityInfo info;

            private DestroiedEntity __next;

            private static DestroiedEntity __head;

            private static DestroiedEntity __pool;

            public static void DisposeAllDestoriedEntities()
            {
                DestroiedEntity head;
                do
                {
                    head = __head;
                    if (head == null)
                        return;

                } while (Interlocked.CompareExchange(ref __head, head.__next, head) != head);

                head.Execute();

                head.Dispose();

                DisposeAllDestoriedEntities();
            }

            public static DestroiedEntity Create(GameObjectEntity instance)
            {
                DestroiedEntity destroiedEntity;
                do
                {
                    destroiedEntity = __pool;
                    if (destroiedEntity == null)
                    {
                        destroiedEntity = new DestroiedEntity();

                        break;
                    }

                } while (System.Threading.Interlocked.CompareExchange(ref __pool, destroiedEntity.__next, destroiedEntity) != destroiedEntity);

                destroiedEntity.__Init(instance);

                return destroiedEntity;
            }

            public void Dispose()
            {
                do
                {
                    __next = __pool;
                } while (System.Threading.Interlocked.CompareExchange(ref __pool, this, __next) != __next);
            }

            public void AsManaged()
            {
                do
                {
                    __next = __head;
                } while (System.Threading.Interlocked.CompareExchange(ref __head, this, __next) != __next);
            }

            private void __Init(GameObjectEntity instance)
            {
                instanceID = instance.__instanceID;
                status = instance.status;
                entity = instance.__entity;
                info = instance.__info;
            }
        }

        internal Action __onCreated = null;

        [SerializeField]
        internal string _worldName;

        [SerializeField, HideInInspector]
        private GameObjectEntityInfo __info;

        [SerializeField]
        internal GameObjectEntity _parent;

        private GameObjectEntity __next;

        private Entity __entity;

        private int __instanceID;

        private bool __isActive;

        private static volatile GameObjectEntity __deserializedEntities = null;

        private static Entity __prefab;

        //private static ConcurrentDictionary<int, GameObjectEntityInfo> __instancedEntities = new ConcurrentDictionary<int, GameObjectEntityInfo>();
        //private readonly static ConcurrentBag<DestroiedEntity> DestoriedEntities = new ConcurrentBag<DestroiedEntity>();

        private readonly static List<ComponentType> ComponentTypeList = new List<ComponentType>();

        //private static ConcurrentDictionary<int, GameObjectEntityInfo> __infos = new ConcurrentDictionary<int, GameObjectEntityInfo>();
        //private static Dictionary<Scene, LinkedList<GameObjectEntity>> __sceneEntities = null;
        //private LinkedListNode<GameObjectEntity> __sceneLinkedListNode;

        /*[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void __Init()
        {
            SceneManager.sceneUnloaded += __DestroyEntities;
        }

        public static void __DestroyEntities(Scene scene)
        {
            if (__sceneEntities == null || !__sceneEntities.TryGetValue(scene, out var sceneEntities))
                return;

            LinkedListNode<GameObjectEntity> sceneLinkedListNode = sceneEntities == null ? null : sceneEntities.First;
            while(sceneLinkedListNode != null)
            {
                sceneLinkedListNode.Value.OnDestroy();

                sceneLinkedListNode = sceneEntities.First;
            }
        }*/

        private static bool __CreateDeserializedEntity(ref DeserializedType type)
        {
            GameObjectEntity deserializedEntity;
            do
            {
                deserializedEntity = __deserializedEntities;
            } while ((object)deserializedEntity != null && Interlocked.CompareExchange(ref __deserializedEntities, deserializedEntity.__next, deserializedEntity) != deserializedEntity);

            if (deserializedEntity == null)
                return (object)deserializedEntity == null ? false : __CreateDeserializedEntity(ref type);

            deserializedEntity.__next = null;

            if (deserializedEntity.status != GameObjectEntityStatus.Deserializing)
                return __CreateDeserializedEntity(ref type);

            if (!deserializedEntity.isInstance)
            {
                if (type == DeserializedType.InstanceOnly)
                {
                    bool result = __CreateDeserializedEntity(ref type);

                    deserializedEntity.__Deserialize();

                    return result;
                }
                else
                {
                    if (type != DeserializedType.IgnoreSceneLoading)
                    {
                        bool result;
                        int sceneCount = SceneManager.sceneCount;
                        for (int i = 0; i < sceneCount; ++i)
                        {
                            if (!SceneManager.GetSceneAt(i).isLoaded)
                            {
                                type = DeserializedType.InstanceOnly;

                                result = __CreateDeserializedEntity(ref type);

                                deserializedEntity.__Deserialize();

                                return result;
                            }
                        }

                        type = DeserializedType.IgnoreSceneLoading;
                    }

                    var scene = deserializedEntity == null ? default : deserializedEntity.gameObject.scene;
                    if (scene.IsValid())
                    {
                        /*if (__sceneEntities == null)
                            __sceneEntities = new Dictionary<Scene, LinkedList<GameObjectEntity>>();

                        if(!__sceneEntities.TryGetValue(scene, out var sceneEntities))
                        {
                            sceneEntities = new LinkedList<GameObjectEntity>();

                            __sceneEntities[scene] = sceneEntities;
                        }

                        deserializedEntity.__sceneLinkedListNode = sceneEntities.AddLast(deserializedEntity);*/
                        deserializedEntity.__BuildArchetypeIfNeed(false);
                    }
                    else
                    {
                        bool result = __CreateDeserializedEntity(ref type);

                        deserializedEntity.status = GameObjectEntityStatus.Invalid;

                        //Debug.LogError("Invalid GameObject Entity!", deserializedEntity);

                        return result;
                    }
                }
            }

            deserializedEntity.__Rebuild();

            return true;
        }

        public static bool CreateAllDeserializedEntities()
        {
            //Debug.Log($"DeserializedEntities {Time.frameCount}");

            var type = DeserializedType.Normal;
            while (__CreateDeserializedEntity(ref type)) ;
            /*bool isSceneLoading = true;
            int sceneCount, i;
            EntityArchetype archetype;
            lock (__deserializedEntities)
            {
                foreach(var deserializedEntity in __deserializedEntities)
                {
                    if (deserializedEntity == null || deserializedEntity.isAwake)
                        continue;

                    UnityEngine.Assertions.Assert.AreEqual(Status.None, deserializedEntity.status);

                    if (deserializedEntity.isInstance)
                        archetype = deserializedEntity.__info.entityArchetype;
                    else
                    {
                        if (isSceneLoading)
                        {
                            sceneCount = SceneManager.sceneCount;
                            for (i = 0; i < sceneCount; ++i)
                            {
                                if (!SceneManager.GetSceneAt(i).isLoaded)
                                    return;
                            }

                            isSceneLoading = false;
                        }

                        if (deserializedEntity != null && deserializedEntity.gameObject.scene.IsValid())
                            archetype = deserializedEntity.__GetOrBuildArchetype();
                        else
                            continue;
                    }

                    deserializedEntity.status = Status.Creating;

                    deserializedEntity.CreateEntity(archetype, deserializedEntity.__CreateAndInit);
                }

                __deserializedEntities.Clear();
            }*/

            return __deserializedEntities == null;
        }

        public static bool IsAllEntitiesDeserialized(DeserializedType type)
        {
            bool isSceneLoading = false;
            if (type == DeserializedType.Normal)
            {
                int sceneCount = SceneManager.sceneCount;
                for (int i = 0; i < sceneCount; ++i)
                {
                    if (!SceneManager.GetSceneAt(i).isLoaded)
                    {
                        isSceneLoading = true;

                        break;
                    }
                }
            }

            for (var deserializedEntity = __deserializedEntities; (object)deserializedEntity != null; deserializedEntity = deserializedEntity.__next)
            {
                if (deserializedEntity.isInstance)
                {
                    //Debug.LogError($"{deserializedEntity.name} : {Time.frameCount}", deserializedEntity);

                    return false;
                }

                if (type != DeserializedType.InstanceOnly)
                {
                    if (!isSceneLoading || deserializedEntity != null && !deserializedEntity.gameObject.scene.IsValid())
                        return false;
                }
            }

            return true;
        }

        /*public static void DisposeAllDestoriedEntities()
        {
            while (DestoriedEntities.TryTake(out var destroiedEntity))
                destroiedEntity.Execute();
        }*/

        public static UnityEngine.Object Instantiate(UnityEngine.Object component, Transform parentin, in Vector3 position, in Quaternion rotation, in Entity prefab)
        {
            __prefab = prefab;

            var target = Instantiate(component, position, rotation, parentin);

            __prefab = Entity.Null;

            return target;
        }

        public static T Instantiate<T>(T component, Transform parentin, in Vector3 position, in Quaternion rotation, in Entity prefab) where T : UnityEngine.Object
        {
            return Instantiate((UnityEngine.Object)component, parentin, position, rotation, prefab) as T;
        }

        public event Action onCreated
        {
            add
            {
                if (isCreated)
                    value();

                __onCreated += value;
            }

            remove
            {
                __onCreated -= value;
            }
        }

        public bool isInstance { get; private set; }

        public bool isActive
        {
            get => __isActive;

            private set
            {
                if (__isActive == value || __entity == Entity.Null)
                    return;

                GameObjectEntityUtility._Add<GameObjectEntityActiveCount>(world, __entity, status, value ? 1 : -1);

                __isActive = value;
            }
        }

        public bool isCreated
        {
            get
            {
                if (status == GameObjectEntityStatus.Created)
                {
                    var world = this.world;
                    if (world != null)
                        return world.IsCreated;
                }

                return false;
            }
        }

        public bool isAssigned
        {
            get
            {
                if (status == GameObjectEntityStatus.Creating || status == GameObjectEntityStatus.Created)
                {
                    var world = this.world;
                    if (world != null)
                        return world.IsCreated;
                }

                return false;
            }
        }

        public GameObjectEntityStatus status
        {
            get;

            private set;
        }

        public Entity entity
        {
            get
            {
                /*if (status != GameObjectEntityStatus.Created)
                    this.ExecuteAllCommands();

                UnityEngine.Assertions.Assert.IsFalse(__entity.Index < 0, $"{name} : {status} : {__entity}");

                UnityEngine.Assertions.Assert.AreNotEqual(Entity.Null, __entity, $"{name} : {status} : {__entity}");*/

                __ForceBuildIfNeed();

                UnityEngine.Assertions.Assert.AreNotEqual(Entity.Null, __entity, $"{name} : {status} : {__entity}");

                return __entity;
            }
        }

        public string worldName
        {
            get
            {
                return _worldName;
            }

            set
            {
                if (isCreated)
                    throw new InvalidOperationException();

                _worldName = value;
            }

            /*set
            {
                if (_worldName == value)
                    return;

                UnityEngine.Assertions.Assert.AreNotEqual(Status.Creating, status);
#if UNITY_EDITOR
                if (UnityEditor.EditorApplication.isPlaying)
#endif
                {
                    if (__entity != Entity.Null)
                    {
                        this.DestroyEntity(__entity);

                        __entity = Entity.Null;
                    }

                    __info = GameObjectEntityInfo.Create(value);
                    if (__data != null && __data.isBuild)
                        __info.Rebuild(__data);
                }

                _worldName = value;
                
                if (isCreated)
                    __Rebuild();
            }*/
        }

        public World world
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    return null;
#endif
                if (__info == null || !__info.isValid)
                {
                    __instanceID = GetInstanceID();

                    if (__info != null && __info.instanceID == __instanceID)
                        DestroyImmediate(__info);

                    __info = GameObjectEntityInfo.Create(__instanceID, componentHash, _worldName);
                }

                return __info == null ? null : __info.world;
            }
        }

        public EntityManager entityManager
        {
            get
            {
                return world.EntityManager;
            }
        }

        public GameObjectEntity parent
        {
            get
            {
                return _parent;
            }
        }

        public GameObjectEntity root
        {
            get
            {
                if (_parent == null)
                    return this;

                return _parent.root;
            }
        }

        /*internal DestroiedEntity destroiedEntity
        {
            get
            {
                DestroiedEntity destroiedEntity;
                destroiedEntity.instanceID = __instanceID;
                destroiedEntity.status = status;
                destroiedEntity.entity = __entity;
                destroiedEntity.info = __info;

                return destroiedEntity;
            }
        }*/

        ~GameObjectEntity()
        {
            if (status != GameObjectEntityStatus.Destroied && (object)__info != null)
                DestroiedEntity.Create(this).AsManaged();
                //DestoriedEntities.Add(destroiedEntity);
        }

        public new bool Contains(Type type)
        {
            __BuildArchetypeIfNeed(false);

            return base.Contains(type);
        }

        public void RebuildArchetype()
        {
            UnityEngine.Assertions.Assert.AreNotEqual(GameObjectEntityStatus.Creating, status);
            if (__entity != Entity.Null)
            {
                using (var destroiedEntity = DestroiedEntity.Create(this))
                    destroiedEntity.Execute();

                __entity = Entity.Null;
            }

            __RebuildArchetype(true);

            if (isCreated)
                __Rebuild();
        }

        protected void Awake()
        {
            __ForceBuildIfNeed();
        }

#if UNITY_EDITOR
        private bool __isNamed;
#endif

        protected void OnEnable()
        {
            isActive = true;

#if UNITY_EDITOR
            if (isCreated && !__isNamed)
            {
                __isNamed = true;

                entityManager.SetName(__entity, name);
            }
#endif
        }

        protected void OnDisable()
        {
            isActive = false;

            /*if (__entity != Entity.Null)
            {
                if (onChanged != null)
                    onChanged.Invoke(Entity.Null);

                this.DestroyEntity(__entity);

                __entity = Entity.Null;
            }*/
        }

        protected void OnDestroy()
        {
            /*if (__sceneLinkedListNode != null)
            {
                __sceneLinkedListNode.List.Remove(__sceneLinkedListNode);

                __sceneLinkedListNode = null;
            }*/

            /*bool isInstance = this.isInstance;
            if (!isInstance)
                __infos.TryRemove(GetInstanceID(), out _);*/

            if ((object)__info != null)
            {
                using (var destroiedEntity = DestroiedEntity.Create(this))
                    destroiedEntity.Execute();

                __info = null;
            }

            __entity = Entity.Null;
            status = GameObjectEntityStatus.Destroied;
        }

        internal void _Create(in Entity entity)
        {
            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{name} : {status} : {entity}");

            UnityEngine.Assertions.Assert.AreNotEqual(Entity.Null, entity, $"{name} : {status} : {entity}");

            UnityEngine.Assertions.Assert.IsFalse(__entity.Index > 0);
            UnityEngine.Assertions.Assert.AreEqual(GameObjectEntityStatus.Creating, status);
            //UnityEngine.Assertions.Assert.AreEqual(Entity.Null, __entity);

            //__info.SetComponents(entity, __data, __components);

            /*if (entity == new Entity() { Index = 26453, Version = 1 })
                Debug.LogError(name, this);*/

#if UNITY_EDITOR
            entityManager.SetName(entity, name);
#endif
            __entity = entity;

            status = GameObjectEntityStatus.Created;

            if (__onCreated != null)
                __onCreated();
        }

        private bool __ForceBuildIfNeed()
        {
            if (__entity == Entity.Null)
            {
                if (status != GameObjectEntityStatus.Creating)
                {
                    UnityEngine.Assertions.Assert.AreEqual(GameObjectEntityStatus.Deserializing, status);

                    __BuildArchetypeIfNeed(false);

                    __Rebuild();
                }

                return true;
            }

            return false;
        }

        private void __Rebuild()
        {
            status = GameObjectEntityStatus.Creating;

            var factory = this.GetFactory();

            ComponentTypeList.Clear();

            GetRuntimeComponentTypes(ComponentTypeList);

            if (_parent == null)
            {
                var parent = transform.parent;
                if (parent != null)
                {
                    _parent = parent.GetComponentInParent<GameObjectEntity>(true);
                    if(_parent != null)
                        ComponentTypeList.Add(ComponentType.ReadOnly<EntityParent>());
                }
            }

            UnityEngine.Assertions.Assert.AreEqual(Entity.Null, __entity);

            __entity = _parent == null ? __prefab : Entity.Null;

            CreateEntityDefinition(__info, ref __entity, ref factory, out var assigner, ComponentTypeList);

            var entityManager = world.EntityManager;
            if (isActiveAndEnabled)
                isActive = true;

            GameObjectEntityUtility._Add<GameObjectEntityInstanceCount>(
                    1,
                    __entity,
                    ref factory, 
                    ref entityManager);

            GameObjectEntityHandle handle;
            handle.value = GCHandle.Alloc(this);
            assigner.SetBuffer(EntityComponentAssigner.BufferOption.Append, __entity, handle);

            EntityOrigin origin;
            origin.entity = __entity;
            assigner.SetComponentData(__entity, origin);

            if (_parent != null)
            {
                _parent.__ForceBuildIfNeed();

                EntityParent entityParent;
                entityParent.entity = _parent.__entity;
                assigner.SetBuffer(EntityComponentAssigner.BufferOption.Append, __entity, entityParent);

                assigner.SetComponentEnabled<EntityParent>(__entity, true);
            }
        }

        private void __RebuildArchetype(bool isPrefab)
        {
            var data = Rebuild();

            if (__info == null || !__info.isValid || __info.componentHash != componentHash)
            {
                __instanceID = GetInstanceID();
                if (__info != null && __info.instanceID == __instanceID)
                    __info.Destroy();

                __info = GameObjectEntityInfo.Create(__instanceID, componentHash, _worldName);
                __info.name = name;
            }

            if (_parent == null)
            {
                var parent = transform.parent;
                if (parent != null)
                    _parent = parent.GetComponentInParent<GameObjectEntity>(true);
            }

            if (_parent == null)
                __info.Rebuild(
                    isPrefab, 
                    data,
                    GameObjectEntityUtility.ComponentTypes);
            else
                __info.Rebuild(
                    isPrefab, 
                    data,
                    GameObjectEntityUtility.ComponentTypesWithParent);

            /*if(isPrefab)
                __infos[GetInstanceID()] = __info;*/
        }

        private void __BuildArchetypeIfNeed(bool isPrefab)
        {
            if (__info == null || !__info.isValid)
            {
                /*if (isPrefab && __infos.TryGetValue(GetInstanceID(), out __info) && __info != null && __info.isValid)
                {
                    Rebuild();

                    if(componentHash == __info.componentHash)
                        return;
                }*/

                __RebuildArchetype(isPrefab);
            }
        }

        private void __Deserialize()
        {
            /*if (isInstance)
                Debug.Log("Deserialize", this);*/

            UnityEngine.Assertions.Assert.IsNull(__next);
            do
            {
                __next = __deserializedEntities;
            } while (Interlocked.CompareExchange(ref __deserializedEntities, this, __next) != __next);
        }

        //Entity IGameObjectEntity.entity => __entity;

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
#if UNITY_EDITOR
            for (var deserializedEntity = __deserializedEntities; deserializedEntity != null; deserializedEntity = deserializedEntity.__next)
            {
                if (deserializedEntity == this)
                    return;
            }

            switch (status)
            {
                case GameObjectEntityStatus.None:
                    break;
                /*case GameObjectEntityStatus.Deserializing:
                    for (var deserializedEntity = __deserializedEntities; deserializedEntity != null; deserializedEntity = deserializedEntity.__next)
                    {
                        if (deserializedEntity == this)
                            return;
                    }

                    __next = null;
                    break;*/
                case GameObjectEntityStatus.Creating:
                case GameObjectEntityStatus.Created:
                    if (__info != null && __info.isValid)
                        return;
                    break;
                default:
                    return;
            }
#else
            if(GameObjectEntityStatus.None != status)
                return;
#endif


            status = GameObjectEntityStatus.Deserializing;

            isInstance = __info != null && __info.isValid;

            __Deserialize();

            /*EntityArchetype archetype = __info == null ? default : __info.entityArchetype;
            if (archetype.Valid)
            {
                status = Status.Creating;

                this.CreateEntity(archetype, __CreateAndInit);
            }*/
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
                return;
#endif

            //Debug.Log($"Deserialized {name}", this);
            //_parent = null;

            __BuildArchetypeIfNeed(!gameObject.scene.IsValid());
        }
    }

    public static partial class GameObjectEntityUtility
    {
        internal static ComponentType[] ComponentTypes = new ComponentType[]
        {
            ComponentType.ReadOnly<GameObjectEntityHandle>(),
            ComponentType.ReadOnly<GameObjectEntityInstanceCount>(),
            ComponentType.ReadOnly<GameObjectEntityActiveCount>(),
            ComponentType.ReadOnly<EntityOrigin>()
        };

        internal static ComponentType[] ComponentTypesWithParent = new ComponentType[]
        {
            ComponentType.ReadOnly<GameObjectEntityHandle>(),
            ComponentType.ReadOnly<GameObjectEntityInstanceCount>(),
            ComponentType.ReadOnly<GameObjectEntityActiveCount>(),
            ComponentType.ReadOnly<EntityOrigin>(),
            ComponentType.ReadOnly<EntityParent>()
        };

        public static ref readonly Unity.Core.TimeData GetTimeData(this IGameObjectEntity gameObjectEntity)
        {
            return ref  __GetCommandSystem(gameObjectEntity).World.Time;
        }

        public static void ExecuteAllCommands(this IGameObjectEntity gameObjectEntity)
        {
#if UNITY_EDITOR
            Debug.LogWarning("Execute All Game Object Entity Commands");
#endif

            //var world = gameObjectEntity.world;
            //gameObjectEntity.world.GetExistingSystem<BeginFrameEntityCommandSystem>().Update(world.Unmanaged);
            __GetCommandSystem(gameObjectEntity).Update();
        }

        public static EntityCommandFactory GetFactory(this IGameObjectEntity gameObjectEntity)
        {
            return __GetCommandSystem(gameObjectEntity).factory;
        }

        public static void DestroyEntity(this IGameObjectEntity gameObjectEntity, NativeArray<Entity> entities)
        {
            __GetCommandSystem(gameObjectEntity).DestroyEntity(entities);
        }

        /*public static void AppendBuffer<T>(this IGameObjectEntity gameObjectEntity, in Entity entity, params T[] values) where T : unmanaged, IBufferElementData
        {
            __GetCommandSystem(gameObjectEntity).AppendBuffer<T, T[]>(entity, values);
        }*/

        public static void SetComponentData<T>(this IGameObjectEntity gameObjectEntity, in Entity entity, T value) where T : struct, IComponentData
        {
            __GetCommandSystem(gameObjectEntity).SetComponentData(entity, value);
        }

        public static void SetBuffer<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, in Entity entity, TCollection values)
            where TValue : struct, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            __GetCommandSystem(gameObjectEntity).SetBuffer<TValue, TCollection>(entity, values);
        }

        public static void SetComponentEnabled<T>(this IGameObjectEntity gameObjectEntity, in Entity entity, bool value)
            where T : unmanaged, IEnableableComponent
        {
            __GetCommandSystem(gameObjectEntity).SetComponentEnabled<T>(entity, value);
        }

        /*public static void SetSharedComponentData<T>(this IGameObjectEntity gameObjectEntity, T value) where T : struct, ISharedComponentData
        {
            __GetCommandSystem(gameObjectEntity).SetSharedComponentData(gameObjectEntity.entity, value);
        }
        
        public static void SetComponentObject<T>(this IGameObjectEntity gameObjectEntity, EntityObject<T> value)
        {
            __GetCommandSystem(gameObjectEntity).SetComponentObject(gameObjectEntity.entity, value);
        }*/

        public static bool TryGetComponentData<T>(this IGameObjectEntity gameObjectEntity, Entity entity, out T value) where T : unmanaged, IComponentData
        {
            value = default;
            return __GetCommandSystem(gameObjectEntity).TryGetComponentData(entity, ref value);
        }

        public static bool TryGetBuffer<T>(this IGameObjectEntity gameObjectEntity, in Entity entity, int index, out T value) where T : unmanaged, IBufferElementData
        {
            value = default;
            return __GetCommandSystem(gameObjectEntity).TryGetBuffer(entity, index, ref value);
        }

        public static bool TryGetBuffer<TValue, TList, TWrapper>(
            this IGameObjectEntity gameObjectEntity,
            in Entity entity, 
            ref TList list,
            ref TWrapper wrapper)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
        {
            return __GetCommandSystem(gameObjectEntity).TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper);
        }

        public static bool TryGetComponentObject<T>(this IGameObjectEntity gameObjectEntity, Entity entity, out T value)
        {
            return __GetCommandSystem(gameObjectEntity).TryGetComponentObject(entity, out value);
        }

        public static T GetComponentData<T>(this IGameObjectEntity gameObjectEntity, Entity entity) where T : unmanaged, IComponentData
        {
            T value = default;
            __GetCommandSystem(gameObjectEntity).TryGetComponentData<T>(entity, ref value);

            return value;
        }

        public static T[] GetBuffer<T>(this IGameObjectEntity gameObjectEntity, Entity entity) where T : unmanaged, IBufferElementData
        {
            var list = new NativeList<T>(Allocator.Temp);
            NativeListWriteOnlyWrapper<T> wrapper;
            if (__GetCommandSystem(gameObjectEntity).TryGetBuffer<T, NativeList<T>, NativeListWriteOnlyWrapper<T>>(entity, ref list, ref wrapper))
            {
                int length = list.Length;
                if (length > 0)
                {
                    var result = new T[length];
                    for (int i = 0; i < length; ++i)
                        result[i] = list[i];

                    list.Dispose();

                    return result;
                }
            }
            list.Dispose();

            return null;
        }

        public static T GetBuffer<T>(this IGameObjectEntity gameObjectEntity, Entity entity, int index) where T : unmanaged, IBufferElementData
        {
            T value = default;
            bool result = __GetCommandSystem(gameObjectEntity).TryGetBuffer<T>(entity, index, ref value);

            UnityEngine.Assertions.Assert.IsTrue(result);

            return value;
        }

        public static T GetComponentObject<T>(this IGameObjectEntity gameObjectEntity, Entity entity) where T : class
        {
            return __GetCommandSystem(gameObjectEntity).TryGetComponentObject(entity, out T value) ? value : null;
        }

        public static bool TryGetSharedComponentData<T>(this IGameObjectEntity gameObjectEntity, in Entity entity, out T value) where T : struct, ISharedComponentData
        {
            return __GetCommandSystem(gameObjectEntity).TryGetSharedComponentData(entity, out value);
        }

        public static bool TryGetSharedComponentData<T>(this IGameObjectEntity gameObjectEntity, out T value) where T : struct, ISharedComponentData
        {
            return __GetCommandSystem(gameObjectEntity).TryGetSharedComponentData(gameObjectEntity.entity, out value);
        }

        public static bool HasComponent<T>(this IGameObjectEntity gameObjectEntity, Entity entity)
        {
            return __GetCommandSystem(gameObjectEntity).HasComponent<T>(entity, out _);
        }

        internal static void Execute(this GameObjectEntity.DestroiedEntity destroiedEntity)
        {
            _Add<GameObjectEntityInstanceCount>(destroiedEntity.info.world, destroiedEntity.entity, destroiedEntity.status, -1);

            if (destroiedEntity.info.instanceID == destroiedEntity.instanceID)
            {
#if UNITY_EDITOR
                if (Application.isPlaying)
#endif
                    UnityEngine.Object.Destroy(destroiedEntity.info);
            }
        }

        internal static void Destroy(this GameObjectEntityInfo info)
        {
            if (info == null)
                return;

            if (info.isValidPrefab)
            {
                var world = info.world;
                if (world != null && world.IsCreated && info.prefab != Entity.Null)
                    __GetCommandSystem(world).factory.DestroyEntity(info.prefab);

                info.SetPrefab(Entity.Null);
            }

            UnityEngine.Object.DestroyImmediate(info);
        }

        internal static void _Add<T>(World world, in Entity entity, GameObjectEntityStatus status, int value) where T : unmanaged, IComponentData, IEnableableComponent, IGameObjectEntityStatus
        {
            if (world == null || !world.IsCreated)
                return;

            var commandSystem = __GetCommandSystem(world);
            Entity origin;
            switch (status)
            {
                case GameObjectEntityStatus.Creating:
                    origin = entity;
                    break;
                case GameObjectEntityStatus.Created:
                    origin = world.EntityManager.GetComponentData<EntityOrigin>(entity).entity;
                    break;
                default:
                    if(entity == Entity.Null)
                        return;

                    throw new InvalidOperationException($"{entity} : {status} : {typeof(T)}");
                    //以下情况不对因为GameObjectEntityFactorySystem更新顺序在EntityCommanderSystem之前
                    /*case GameObjectEntityStatus.Created:
                        T componentData = default;
                        if (commandSystem.TryGetComponentData(entity, ref componentData))
                        {
                            componentData.value += value;
                            commandSystem.SetComponentData(entity, componentData);
                            commandSystem.SetComponentEnabled<T>(entity, true);
                        }
                        break;*/
            }

            var factory = commandSystem.factory;
            _Add<T, EntityCommandSharedSystemGroup>(
                commandSystem,
                value,
                origin,
                ref factory);
        }

        internal static void _Add<T>(
            int value,
            in Entity entity,
            ref EntityCommandFactory factory,
            ref EntityManager entityManager) where T : unmanaged, IComponentData, IEnableableComponent, IGameObjectEntityStatus
        {
            var commandSystem = __GetCommandSystem(entityManager.World);

            _Add<T, EntityCommandSharedSystemGroup>(commandSystem, value, entity, ref factory);
        }

        internal static void _Add<TValue, TScheduler>(
            in TScheduler entityManager, 
            int value, 
            in Entity entity, 
            ref EntityCommandFactory factory) 
            where TValue : unmanaged, IComponentData, IEnableableComponent, IGameObjectEntityStatus
            where TScheduler : IEntityCommandScheduler
        {
            if (!__TryGetComponentData(entityManager, factory, entity, out TValue componentData))
            {
                throw new InvalidOperationException($"{entity} : {typeof(TValue)}");
                /*Debug.LogError($"Can not add {typeof(TValue)}");

                return;*/
            }
            
            componentData.value += value;

            UnityEngine.Assertions.Assert.IsFalse(componentData.value < 0);

            //Debug.Log($"Add {typeof(TValue)} : {componentData.value} : {entityOrigin.entity} : {entity}");

            var assigner = factory.instanceAssigner;
            assigner.SetComponentData(entity, componentData);
            assigner.SetComponentEnabled<TValue>(entity, true);
        }

    }
}