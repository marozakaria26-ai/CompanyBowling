package com.company.bowling.controller

import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.widget.Button
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.company.bowling.controller.service.SensorService
import kotlinx.coroutines.launch

class MainActivity : AppCompatActivity() {

    private lateinit var scanQrButton: Button
    private lateinit var statusText: TextView
    private lateinit var gatewayUrlText: TextView
    private lateinit var sessionTokenText: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        scanQrButton = findViewById(R.id.scanQrButton)
        statusText = findViewById(R.id.statusText)
        gatewayUrlText = findViewById(R.id.gatewayUrlText)
        sessionTokenText = findViewById(R.id.sessionTokenText)

        scanQrButton.setOnClickListener {
            startActivity(Intent(this, ScanActivity::class.java))
        }

        checkForSessionIntent()
    }

    private fun checkForSessionIntent() {
        intent?.getStringExtra("session_token")?.let { token ->
            intent?.getStringExtra("gateway_url")?.let { url ->
                sessionTokenText.text = "Token: ${token.take(8)}..."
                gatewayUrlText.text = "Gateway: $url"
                startControllerSession(token, url)
            }
        }
    }

    private fun startControllerSession(token: String, url: String) {
        val intent = Intent(this, ControllerActivity::class.java).apply {
            putExtra(SensorService.EXTRA_SESSION_TOKEN, token)
            putExtra(SensorService.EXTRA_GATEWAY_URL, url)
        }
        startActivity(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        checkForSessionIntent()
    }
}