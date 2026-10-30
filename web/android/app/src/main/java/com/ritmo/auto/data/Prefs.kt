package com.ritmo.auto.data

import android.content.Context
import android.content.SharedPreferences
import org.json.JSONArray

object Prefs {

    private const val FILE = "ritmo_prefs"

    private lateinit var sp: SharedPreferences

    fun init(context: Context) {
        if (::sp.isInitialized) return
        sp = context.applicationContext.getSharedPreferences(FILE, Context.MODE_PRIVATE)
    }

    var wifiOnly: Boolean
        get() = sp.getBoolean("wifi_only", false)
        set(value) = sp.edit().putBoolean("wifi_only", value).apply()

    var notificationAsked: Boolean
        get() = sp.getBoolean("notif_asked", false)
        set(value) = sp.edit().putBoolean("notif_asked", value).apply()

    var lastQuery: String
        get() = sp.getString("last_query", "").orEmpty()
        set(value) = sp.edit().putString("last_query", value).apply()

    fun recentQueries(): List<String> {
        val raw = sp.getString("recent_queries", null) ?: return emptyList()
        return try {
            val arr = JSONArray(raw)
            val out = ArrayList<String>(arr.length())
            for (i in 0 until arr.length()) arr.optString(i).takeIf { it.isNotBlank() }?.let { out.add(it) }
            out
        } catch (t: Throwable) {
            emptyList()
        }
    }

    fun addRecentQuery(query: String) {
        if (query.isBlank()) return
        val list = recentQueries().toMutableList()
        list.remove(query)
        list.add(0, query)
        while (list.size > 8) list.removeAt(list.size - 1)
        val arr = JSONArray()
        list.forEach { arr.put(it) }
        sp.edit().putString("recent_queries", arr.toString()).apply()
    }
}
