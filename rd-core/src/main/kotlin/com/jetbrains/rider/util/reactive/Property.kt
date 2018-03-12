package com.jetbrains.rider.util.reactive

import com.jetbrains.rider.util.lifetime.Lifetime
import com.jetbrains.rider.util.lifetime.plusAssign
import java.time.Duration
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class Property<T>(defaultValue: T) : IProperty<T> {
    override fun set(newValue: T) {
        value = newValue
    }

    override var value: T = defaultValue
        set(newValue) {
            if (field == newValue) return
            field = newValue
            change.fire(newValue)
        }

    override val change = Signal<T>()
}

open class OptProperty<T : Any>() : IOptProperty<T> {

    constructor(defaultValue: T) : this() {
        @Suppress("LeakingThis")
        _value = defaultValue
    }

    override val change = Signal<T>()

    override fun set(newValue: T) {
        if (newValue == _value) return
        _value = newValue
        change.fire(newValue)
    }

    protected var _value: T? = null

    override val valueOrNull: T?
        get() = _value
}

class Trigger<T : Any>() : OptProperty<T>(), IMutableTrigger<T> {
    constructor(v: T) : this() {
        set(v)
    }

    override fun set(newValue: T) {
        if (_value != null && _value != newValue) {
            throw IllegalStateException("Trigger already set with `$_value`, but you try to rewrite it to `$newValue`")
        }

        super.set(newValue)
    }

    override fun wait(cancellationToken: Lifetime, timeout: Duration, pump: Pump?): Boolean {
        //short circuit
        if (cancellationToken.isTerminated) return false
        if (hasValue) return true

        val lock = CountDownLatch(1)
        cancellationToken += {lock.countDown()}

        adviseOnce(cancellationToken) { lock.countDown() }
        if (cancellationToken.isTerminated) return false

        if (pump == null)
            return lock.await(timeout.toNanos(), TimeUnit.NANOSECONDS) && hasValue

        val stopTime = System.nanoTime() + timeout.toNanos()

        while (System.nanoTime() < stopTime && !cancellationToken.isTerminated) {
            pump.pumpAction()

            val awaitTimeNanos = minOf(stopTime - System.nanoTime(), pump.pumpPause.toNanos())
            if (lock.await(maxOf(awaitTimeNanos, 0), TimeUnit.NANOSECONDS) && hasValue)
                return true
        }
        return false
    }
}