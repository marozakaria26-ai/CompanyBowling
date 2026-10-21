package com.company.bowling.controller.sensor

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import com.company.bowling.controller.model.Vector3
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.channels.ReceiveChannel

class SensorManagerImpl(
    private val context: Context,
    private val targetFrequencyHz: Int = 60
) : SensorEventListener {

    private val sensorManager = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val accelerometer = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)
    private val gyroscope = sensorManager.getDefaultSensor(Sensor.TYPE_GYROSCOPE)
    private val rotationVector = sensorManager.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR)

    private val sensorChannel = Channel<SensorData>(100)
    private var isRunning = false
    private var lastAccelerometer: FloatArray? = null
    private var lastGyroscope: FloatArray? = null
    private var lastRotationVector: FloatArray? = null
    private var lastTimestamp = 0L

    data class SensorData(
        val timestamp: Long,
        val acceleration: Vector3,
        val gyroscope: Vector3,
        val orientation: Vector3?
    )

    fun start(): ReceiveChannel<SensorData> {
        if (isRunning) return sensorChannel

        val accelerometerOk = accelerometer != null && sensorManager.registerListener(
            this, accelerometer, calculateDelay()
        )
        val gyroscopeOk = gyroscope != null && sensorManager.registerListener(
            this, gyroscope, calculateDelay()
        )
        val rotationOk = rotationVector != null && sensorManager.registerListener(
            this, rotationVector, calculateDelay()
        )

        isRunning = accelerometerOk || gyroscopeOk
        return sensorChannel
    }

    fun stop() {
        if (!isRunning) return
        sensorManager.unregisterListener(this)
        isRunning = false
        sensorChannel.close()
    }

    private fun calculateDelay(): Int {
        return when {
            targetFrequencyHz >= 100 -> SensorManager.SENSOR_DELAY_FASTEST
            targetFrequencyHz >= 60 -> SensorManager.SENSOR_DELAY_GAME
            targetFrequencyHz >= 20 -> SensorManager.SENSOR_DELAY_UI
            else -> SensorManager.SENSOR_DELAY_NORMAL
        }
    }

    override fun onSensorChanged(event: SensorEvent) {
        val timestamp = System.currentTimeMillis()
        
        when (event.sensor.type) {
            Sensor.TYPE_ACCELEROMETER -> lastAccelerometer = event.values.copyOf()
            Sensor.TYPE_GYROSCOPE -> lastGyroscope = event.values.copyOf()
            Sensor.TYPE_ROTATION_VECTOR -> lastRotationVector = event.values.copyOf()
        }

        if (lastAccelerometer != null && lastGyroscope != null) {
            val acceleration = Vector3(
                lastAccelerometer[0].toDouble(),
                lastAccelerometer[1].toDouble(),
                lastAccelerometer[2].toDouble()
            )
            
            val gyroscope = Vector3(
                lastGyroscope[0].toDouble(),
                lastGyroscope[1].toDouble(),
                lastGyroscope[2].toDouble()
            )

            val orientation = lastRotationVector?.let { rv ->
                val rotationMatrix = FloatArray(9)
                SensorManager.getRotationMatrixFromVector(rotationMatrix, rv)
                val orientationVals = FloatArray(3)
                SensorManager.getOrientation(rotationMatrix, orientationVals)
                Vector3(
                    orientationVals[0].toDouble(),
                    orientationVals[1].toDouble(),
                    orientationVals[2].toDouble()
                )
            }

            val data = SensorData(timestamp, acceleration, gyroscope, orientation)
            sensorChannel.trySend(data)
            lastTimestamp = timestamp
        }
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) {}
}