package com.company.bowling.controller.sensor

import com.company.bowling.controller.model.ThrowParameters
import com.company.bowling.controller.model.Vector3
import kotlinx.coroutines.channels.Channel

enum class ThrowState {
    IDLE, READY, BACKSWING, FORWARD_SWING, RELEASE, COOLDOWN
}

data class ThrowResult(
    val isValid: Boolean,
    val parameters: ThrowParameters? = null,
    val rejectionReason: String? = null,
    val finalState: ThrowState,
    val durationMs: Long = 0
)

class ThrowDetector(
    private val config: ThrowDetectorConfig = ThrowDetectorConfig()
) {

    private var state = ThrowState.IDLE
    private val frameBuffer = mutableListOf<SensorFrame>()
    private val throwFrames = mutableListOf<SensorFrame>()
    private var stateStartTime = 0L
    private var throwStartTime = 0L
    private var gravityVector = Vector3(0.0, -9.80665, 0.0)
    private var gravityInitialized = false
    private var peakAcceleration = 0.0
    private var peakAccelerationVector = Vector3(0.0, 0.0, 0.0)
    private var peakGyroscope = 0.0
    private var lastReleaseTime = 0L

    data class SensorFrame(
        val timestamp: Long,
        val acceleration: Vector3,
        val gyroscope: Vector3,
        val linearAcceleration: Vector3,
        val accelMagnitude: Double,
        val gyroMagnitude: Double
    )

    fun processFrame(timestamp: Long, acceleration: Vector3, gyroscope: Vector3): ThrowResult {
        if (!gravityInitialized) {
            initializeGravity(acceleration)
            return ThrowResult(false, finalState = state)
        }

        val linearAccel = subtractVectors(acceleration, gravityVector)
        val accelMag = magnitude(linearAccel)
        val gyroMag = magnitude(gyroscope)

        if (accelMag > peakAcceleration) {
            peakAcceleration = accelMag
            peakAccelerationVector = linearAccel
        }
        if (gyroMag > peakGyroscope) {
            peakGyroscope = gyroMag
        }

        frameBuffer.add(SensorFrame(timestamp, acceleration, gyroscope, linearAccel, accelMag, gyroMag))
        if (frameBuffer.size > config.smoothingWindowSize) {
            frameBuffer.removeAt(0)
        }

        val smoothedAccel = smoothAcceleration()
        val smoothedGyro = smoothGyroscope()
        val now = timestamp
        val timeInState = now - stateStartTime
        val totalThrowTime = if (throwStartTime > 0) now - throwStartTime else 0L

        return when (state) {
            ThrowState.IDLE -> handleIdle(smoothedAccel, smoothedGyro, timeInState, now)
            ThrowState.READY -> handleReady(smoothedAccel, smoothedGyro, timeInState, now)
            ThrowState.BACKSWING -> handleBackswing(smoothedAccel, smoothedGyro, timeInState, totalThrowTime, now)
            ThrowState.FORWARD_SWING -> handleForwardSwing(smoothedAccel, smoothedGyro, timeInState, totalThrowTime, now)
            ThrowState.RELEASE -> handleRelease(smoothedAccel, smoothedGyro, timeInState, totalThrowTime, now)
            ThrowState.COOLDOWN -> handleCooldown(timeInState, now)
        }
    }

    private fun initializeGravity(acceleration: Vector3) {
        val mag = magnitude(acceleration)
        if (mag > 9.80665 * 0.8 && mag < 9.80665 * 1.2) {
            gravityVector = normalize(acceleration)
            gravityInitialized = true
        }
    }

    private fun handleIdle(accel: Vector3, gyro: Vector3, timeInState: Long, now: Long): ThrowResult {
        if (accel.y > config.idleAccelerationThreshold) {
            transitionTo(ThrowState.READY, now)
        }
        return ThrowResult(false, finalState = state)
    }

    private fun handleReady(accel: Vector3, gyro: Vector3, timeInState: Long, now: Long): ThrowResult {
        if (accel.y < -config.backswingAccelerationThreshold) {
            transitionTo(ThrowState.BACKSWING, now)
            throwStartTime = now
            peakAcceleration = 0.0
            peakGyroscope = 0.0
            throwFrames.clear()
        } else if (accel.y < config.idleAccelerationThreshold * 0.5) {
            transitionTo(ThrowState.IDLE, now)
        }
        return ThrowResult(false, finalState = state)
    }

    private fun handleBackswing(accel: Vector3, gyro: Vector3, timeInState: Long, totalTime: Long, now: Long): ThrowResult {
        throwFrames.add(SensorFrame(now, accel, gyro, accel, magnitude(accel), magnitude(gyro)))

        if (timeInState > config.maxBackswingDurationMs) {
            transitionTo(ThrowState.IDLE, now)
            return ThrowResult(false, finalState = state)
        }

        if (accel.y > config.forwardSwingAccelerationThreshold && timeInState >= config.minBackswingDurationMs) {
            transitionTo(ThrowState.FORWARD_SWING, now)
        }
        return ThrowResult(false, finalState = state)
    }

    private fun handleForwardSwing(accel: Vector3, gyro: Vector3, timeInState: Long, totalTime: Long, now: Long): ThrowResult {
        throwFrames.add(SensorFrame(now, accel, gyro, accel, magnitude(accel), magnitude(gyro)))

        if (totalTime > config.maxTotalThrowDurationMs || timeInState > config.maxForwardSwingDurationMs) {
            transitionTo(ThrowState.IDLE, now)
            return ThrowResult(false, finalState = state)
        }

        val forwardAccel = accel.y
        val gyroMag = magnitude(gyro)
        
        if (forwardAccel < config.forwardSwingAccelerationThreshold * 0.3 && 
            gyroMag > config.releaseGyroscopeThreshold &&
            timeInState >= config.minForwardSwingDurationMs) {
            transitionTo(ThrowState.RELEASE, now)
        }
        return ThrowResult(false, finalState = state)
    }

    private fun handleRelease(accel: Vector3, gyro: Vector3, timeInState: Long, totalTime: Long, now: Long): ThrowResult {
        val params = calculateThrowParameters()
        
        val isValid = params.power >= config.minThrowPower && params.power <= config.maxThrowPower
        val reason = if (!isValid) "Invalid power: ${params.power}" else null
        
        transitionTo(ThrowState.COOLDOWN, now)
        lastReleaseTime = now
        
        return ThrowResult(isValid, params, reason, state, totalTime)
    }

    private fun handleCooldown(timeInState: Long, now: Long): ThrowResult {
        if (timeInState > 1000) {
            transitionTo(ThrowState.IDLE, now)
        }
        return ThrowResult(false, finalState = state)
    }

    private fun transitionTo(newState: ThrowState, now: Long) {
        state = newState
        stateStartTime = now
    }

    private fun calculateThrowParameters(): ThrowParameters {
        val forwardAccel = peakAccelerationVector.y
        val lateralAccel = peakAccelerationVector.x
        val verticalAccel = peakAccelerationVector.z

        val power = (forwardAccel * config.powerScalingFactor).coerceIn(0.0, 1.0)
        val direction = (lateralAccel * config.directionScalingFactor).coerceIn(-1.0, 1.0)
        val spin = (peakGyroscope * config.spinScalingFactor).coerceIn(-1.0, 1.0)

        val releaseAngle = Math.atan2(verticalAccel, forwardAccel) * 180.0 / Math.PI
            .coerceIn(config.minReleaseAngle, config.maxReleaseAngle)

        val releaseVelocity = Vector3(
            lateralAccel * config.powerScalingFactor,
            forwardAccel * config.powerScalingFactor,
            verticalAccel * config.powerScalingFactor
        )

        return ThrowParameters(
            power = power,
            direction = direction,
            spin = spin,
            releaseAngle = releaseAngle,
            durationMs = 0,
            peakAcceleration = peakAcceleration,
            releaseVelocity = releaseVelocity
        )
    }

    private fun smoothAcceleration(): Vector3 {
        if (frameBuffer.isEmpty()) return Vector3(0.0, 0.0, 0.0)
        var sumX = 0.0; var sumY = 0.0; var sumZ = 0.0
        for (f in frameBuffer) {
            sumX += f.linearAcceleration.x
            sumY += f.linearAcceleration.y
            sumZ += f.linearAcceleration.z
        }
        val count = frameBuffer.size.toDouble()
        return Vector3(sumX / count, sumY / count, sumZ / count)
    }

    private fun smoothGyroscope(): Vector3 {
        if (frameBuffer.isEmpty()) return Vector3(0.0, 0.0, 0.0)
        var sumX = 0.0; var sumY = 0.0; var sumZ = 0.0
        for (f in frameBuffer) {
            sumX += f.gyroscope.x
            sumY += f.gyroscope.y
            sumZ += f.gyroscope.z
        }
        val count = frameBuffer.size.toDouble()
        return Vector3(sumX / count, sumY / count, sumZ / count)
    }

    fun reset() {
        state = ThrowState.IDLE
        frameBuffer.clear()
        throwFrames.clear()
        stateStartTime = 0
        throwStartTime = 0
        peakAcceleration = 0.0
        peakGyroscope = 0.0
        peakAccelerationVector = Vector3(0.0, 0.0, 0.0)
        gravityInitialized = false
    }

    fun getCurrentState(): ThrowState = state

    companion object {
        private fun magnitude(v: Vector3): Double = Math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z)
        private fun normalize(v: Vector3): Vector3 {
            val mag = magnitude(v)
            return if (mag > 0) Vector3(v.x / mag, v.y / mag, v.z / mag) else Vector3(0.0, -1.0, 0.0)
        }
        private fun subtractVectors(a: Vector3, b: Vector3): Vector3 = 
            Vector3(a.x - b.x, a.y - b.y, a.z - b.z)
    }
}

data class ThrowDetectorConfig(
    val idleAccelerationThreshold: Double = 1.5,
    val backswingAccelerationThreshold: Double = 3.0,
    val forwardSwingAccelerationThreshold: Double = 8.0,
    val releaseGyroscopeThreshold: Double = 2.5,
    val minBackswingDurationMs: Long = 300,
    val maxBackswingDurationMs: Long = 2000,
    val minForwardSwingDurationMs: Long = 150,
    val maxForwardSwingDurationMs: Long = 800,
    val maxTotalThrowDurationMs: Long = 3000,
    val smoothingWindowSize: Int = 5,
    val lowPassFilterAlpha: Double = 0.3,
    val powerScalingFactor: Double = 0.12,
    val directionScalingFactor: Double = 0.08,
    val spinScalingFactor: Double = 0.15,
    val minReleaseAngle: Double = -15.0,
    val maxReleaseAngle: Double = 45.0,
    val minThrowPower: Double = 0.15,
    val maxThrowPower: Double = 1.0
)