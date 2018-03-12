package com.jetbrains.rider.framework


import com.jetbrains.rider.framework.base.AbstractBuffer
import java.util.concurrent.atomic.AtomicInteger

enum class IdKind {
    Client,
    Server
}

//PLEASE DO NOT CHANGE IT!!! IT'S EXACTLY THE SAME ON C# SIDE
internal fun String?.getPlatformIndependentHash(initial: Long = 19L) : Long = this?.fold(initial, {acc, c -> acc*31 + c.toInt()}) ?:0
internal fun Int.getPlatformIndependentHash(initial: Long = 19L) : Long = initial*31 + (this + 1)
internal fun Long.getPlatformIndependentHash(initial: Long = 19L) : Long = initial*31 + (this + 1)


/**
 * An identifier of the object that participates in the object graph.
 */
data class RdId(val hash: Long) {

    companion object {
        val allIds = HashSet<Long>()

        val Null : RdId = RdId( 0)
        const val MAX_STATIC_ID = 1000000

        fun read(buffer: AbstractBuffer) : RdId {
            val number = buffer.readLong()
            return RdId(number)
        }
    }


    fun write(buffer: AbstractBuffer) {
        buffer.writeLong(hash)
    }

    val isNull: Boolean get() { return equals(Null) }

    override fun toString(): String {
        return java.lang.Long.toUnsignedString(hash)
    }

    fun notNull() : RdId {
        require(!isNull) {"id is null"}
        return this
    }

    fun mix(tail: String) : RdId {
        return RdId(tail.getPlatformIndependentHash(hash))
    }
    fun mix(tail: Int) : RdId {
        return RdId(tail.getPlatformIndependentHash(hash))
    }

    fun mix(tail: Long) : RdId {
        return RdId(tail.getPlatformIndependentHash(hash))
    }
}

/**
 * Generates unique identifiers for objects in an object graph, supporting separate ID spaces for IDs assigned
 * on the client and the server side of the protocol.
 */
class Identities(dynamicKind : IdKind = IdKind.Client) : IIdentities {
    private val idAcc = AtomicInteger(when(dynamicKind) {
        IdKind.Client -> BASE_CLIENT_ID
        IdKind.Server -> BASE_SERVER_ID
    })

    companion object {
        private const val BASE_CLIENT_ID = RdId.MAX_STATIC_ID
        private const val BASE_SERVER_ID = RdId.MAX_STATIC_ID + 1
    }


    override fun next(parent: RdId): RdId {
        return parent.mix(idAcc.getAndAdd(2))
    }
}