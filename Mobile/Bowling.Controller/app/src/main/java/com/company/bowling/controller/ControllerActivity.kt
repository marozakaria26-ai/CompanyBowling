package com.company.bowling.controller

import android.content.Intent
import android.os.Bundle
import android.widget.Button
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.company.bowling.controller.service.SensorService
import com.company.bowling.controller.sensor.ThrowState

class ControllerActivity : AppCompatActivity() {

    private lateinit var statusText: TextView
    private lateinit var stateText: TextView
    private lateinit var sensorInfoText: TextView
    private lateinit var throwInfoText: TextView
    private lateinit var disconnectButton: Button
    private lateinit var throwDebugText: TextView

    private var sessionToken: String = ""
    private var gatewayUrl: String = ""
    private var sensorServiceIntent: Intent? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_controller)

        statusText = findViewById(R.id.statusText)
        stateText = findViewById(R.id.stateText)
        sensorInfoText = findViewById(R.id.sensorInfoText)
        throwInfoText = findViewById(R.id.throwInfoText)
        disconnectButton = findViewById(R.id.disconnectButton)
        throwDebugText = findViewById(R.id.throwDebugText)

        sessionToken = intent.getStringExtra(SensorService.EXTRA_SESSION_TOKEN) ?: ""
        gatewayUrl = intent.getStringExtra(SensorService.EXTRA_GATEWAY_URL) ?: ""

        if (sessionToken.isEmpty() || gatewayUrl.isEmpty()) {
            Toast.makeText(this, "Missing session data", Toast.LENGTH_SHORT).show()
            finish()
            return
        }

        statusText.text = "Status: Connecting..."
        stateText.text = "State: IDLE"

        disconnectButton.setOnClickListener {
            stopSensorService()
            finish()
        }

        startSensorService()
    }

    private fun startSensorService() {
        sensorServiceIntent = Intent(this, SensorService::class.java).apply {
            action = SensorService.ACTION_START_SESSION
            putExtra(SensorService.EXTRA_SESSION_TOKEN, sessionToken)
            putExtra(SensorService.EXTRA_GATEWAY_URL, gatewayUrl)
        }
        startForegroundService(sensorServiceIntent!!)
    }

    private fun stopSensorService() {
        val intent = Intent(this, SensorService::class.java).apply {
            action = SensorService.ACTION_STOP_SESSION
        }
        startService(intent)
    }

    override fun onDestroy() {
        super.onDestroy()
        stopSensorService()
    }

    fun updateState(state: ThrowState) {
        runOnUiThread {
            stateText.text = "State: $state"
        }
    }

    fun updateSensorInfo(accelX: Double, accelY: Double, accelZ: Double, gyroX: Double, gyroY: Double, gyroZ: Double) {
        runOnUiThread {
            sensorInfoText.text = String.format(
                "Accel: %.2f, %.2f, %.2f\nGyro: %.2f, %.2f, %.2f",
                accelX, accelY, accelZ, gyroX, gyroY, gyroZ
            )
        }
    }

    fun updateThrowInfo(power: Double, direction: Double, spin: Double, angle: Double) {
        runOnUiThread {
            throwInfoText.text = String.format(
                "Throw: Power=%.2f Dir=%.2f Spin=%.2f Angle=%.1f°",
                power, direction, spin, angle
            )
            throwDebugText.text = "LAST THROW DETECTED!"
        }
    }

    fun updateConnectionStatus(connected: Boolean) {
        runOnUiThread {
            statusText.text = "Status: ${if (connected) "Connected" else "Disconnected"}"
        }
    }
}