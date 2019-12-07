package com.jetbrains.rd.framework.test.cases.interning

import com.jetbrains.rd.framework.IProtocol
import com.jetbrains.rd.framework.RdId
import com.jetbrains.rd.framework.impl.InternRoot
import com.jetbrains.rd.framework.test.util.RdFrameworkTestBase
import com.jetbrains.rd.framework.test.util.TestWire
import org.junit.experimental.theories.*
import org.junit.runner.RunWith

@RunWith(Theories::class)
class InterningRemovalsTest : RdFrameworkTestBase() {
    companion object {
        @DataPoint
        @JvmField
        val trueValue = true

        @DataPoint
        @JvmField
        val falseValue = false
    }

    @Theory
    fun doTest(firstSendServer: Boolean, secondSendServer: Boolean, thirdSendServer: Boolean) {
        println("First: $firstSendServer second: $secondSendServer third: $thirdSendServer")
        val rootServer = InternRoot<Any>().bindStatic(serverProtocol,"top")
        val rootClient = InternRoot<Any>().bindStatic(clientProtocol, "top")

        val stringToSend = "This string is nice and long enough to overshadow any interning overheads"

        fun proto(server: Boolean) = if(server) serverProtocol else clientProtocol
        fun root(server: Boolean) = if(server) rootServer else rootClient

        val firstSendBytes = measureBytes(proto(firstSendServer)) {
            root(firstSendServer).intern(stringToSend)
        }

        val secondSendBytes = measureBytes(proto(secondSendServer)) {
            root(secondSendServer).intern(stringToSend)
        }

        assert(secondSendBytes == 0L) { "Re-interning a value should not resend it" }

        val removalSendBytes = measureBytes(proto(false)) {
            root(false).remove(stringToSend)
            root(true).remove(stringToSend)
        }
        assert(removalSendBytes == 0L) { "Removal should not send anything" }

        val thirdSendBytes = measureBytes(proto(thirdSendServer)) {
            root(thirdSendServer).intern(stringToSend)
        }

        assert(thirdSendBytes == firstSendBytes) { "Re-sending removed value uses different amount of bytes, bug? Expected $firstSendBytes, got $thirdSendBytes" }

        println("Removal sent $removalSendBytes")
    }

    private fun measureBytes(protocol: IProtocol, action: () -> Unit): Long {
        val pre = (protocol.wire as TestWire).bytesWritten
        action()
        return (protocol.wire as TestWire).bytesWritten - pre
    }

    private fun <T : Any> InternRoot<T>.bindStatic(protocol: IProtocol, id: String) : InternRoot<T> {
        identify(protocol.identity, RdId.Null.mix(id))
        bind(if(protocol === clientProtocol) clientLifetime else serverLifetime, protocol, id)
        return this
    }
}