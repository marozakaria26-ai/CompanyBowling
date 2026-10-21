package com.company.bowling.controller

import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.widget.Button
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.google.zxing.integration.android.IntentIntegrator
import com.google.zxing.integration.android.IntentResult
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class ScanActivity : AppCompatActivity() {

    private lateinit var scanButton: Button
    private lateinit var resultText: TextView
    private lateinit var manualEntryButton: Button
    private lateinit var tokenInput: android.widget.EditText
    private lateinit var urlInput: android.widget.EditText

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_scan)

        scanButton = findViewById(R.id.scanButton)
        resultText = findViewById(R.id.resultText)
        manualEntryButton = findViewById(R.id.manualEntryButton)
        tokenInput = findViewById(R.id.tokenInput)
        urlInput = findViewById(R.id.urlInput)

        scanButton.setOnClickListener {
            scanQrCode()
        }

        manualEntryButton.setOnClickListener {
            val token = tokenInput.text.toString().trim()
            val url = urlInput.text.toString().trim()
            if (token.isNotEmpty() && url.isNotEmpty()) {
                launchController(token, url)
            } else {
                Toast.makeText(this, "Enter both token and URL", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun scanQrCode() {
        IntentIntegrator(this).apply {
            setDesiredBarcodeFormats(IntentIntegrator.QR_CODE)
            setPrompt("Scan bowling session QR code")
            setCameraId(0)
            setBeepEnabled(true)
            setBarcodeImageEnabled(false)
            initiateScan()
        }
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        
        val result = IntentIntegrator.parseActivityResult(requestCode, resultCode, data)
        if (result != null) {
            if (result.contents != null) {
                parseQrResult(result.contents!!)
            } else {
                Toast.makeText(this, "Scan cancelled", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun parseQrResult(content: String) {
        resultText.text = "Scanned: $content"
        
        // Parse URL to extract token and gateway URL
        // Expected format: http://192.168.x.x:5050/join/TOKEN
        try {
            val uri = android.net.Uri.parse(content)
            val pathSegments = uri.pathSegments
            if (pathSegments.size >= 2 && pathSegments[0] == "join") {
                val token = pathSegments[1]
                val gatewayUrl = "${uri.scheme}://${uri.host}:${uri.port}"
                launchController(token, gatewayUrl)
            } else {
                Toast.makeText(this, "Invalid QR code format", Toast.LENGTH_SHORT).show()
            }
        } catch (e: Exception) {
            Log.e("ScanActivity", "Failed to parse QR: ${e.message}")
            Toast.makeText(this, "Invalid QR code", Toast.LENGTH_SHORT).show()
        }
    }

    private fun launchController(token: String, gatewayUrl: String) {
        val intent = Intent(this, ControllerActivity::class.java).apply {
            putExtra(SensorService.EXTRA_SESSION_TOKEN, token)
            putExtra(SensorService.EXTRA_GATEWAY_URL, gatewayUrl)
        }
        startActivity(intent)
        finish()
    }
}