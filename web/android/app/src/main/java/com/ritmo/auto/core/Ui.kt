package com.ritmo.auto.core

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.view.View
import android.widget.ImageView
import android.widget.Toast
import androidx.core.content.ContextCompat
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import kotlin.math.roundToInt

/** Shared background pool. Small on purpose: this app runs on low-end head units. */
val io: ExecutorService = Executors.newFixedThreadPool(3)

val main: Handler = Handler(Looper.getMainLooper())

fun ui(block: () -> Unit) {
    if (Looper.myLooper() == Looper.getMainLooper()) block() else main.post(block)
}

fun <T> async(work: () -> T, done: (value: T?, error: Throwable?) -> Unit) {
    io.execute {
        var value: T? = null
        var error: Throwable? = null
        try {
            value = work()
        } catch (t: Throwable) {
            error = t
        }
        val v = value
        val e = error
        main.post { done(v, e) }
    }
}

fun Context.dp(value: Int): Int = (value * resources.displayMetrics.density).roundToInt()

fun View.dp(value: Int): Int = context.dp(value)

fun Context.color(res: Int): Int = ContextCompat.getColor(this, res)

fun Context.toast(message: String) {
    ui { Toast.makeText(this, message, Toast.LENGTH_SHORT).show() }
}

fun View.visible(show: Boolean) {
    visibility = if (show) View.VISIBLE else View.GONE
}

fun ImageView.tint(color: Int) {
    setColorFilter(color)
}

fun formatDuration(ms: Long): String {
    if (ms <= 0) return "0:00"
    val total = ms / 1000
    val h = total / 3600
    val m = (total % 3600) / 60
    val s = total % 60
    return if (h > 0) String.format("%d:%02d:%02d", h, m, s) else String.format("%d:%02d", m, s)
}

fun formatSize(bytes: Long): String {
    if (bytes <= 0) return "0 MB"
    val mb = bytes / (1024.0 * 1024.0)
    return if (mb >= 1024) String.format("%.1f GB", mb / 1024.0) else String.format("%.0f MB", mb)
}
