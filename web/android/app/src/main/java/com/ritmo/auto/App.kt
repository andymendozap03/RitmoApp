package com.ritmo.auto

import android.app.Application
import com.ritmo.auto.data.Library
import com.ritmo.auto.data.Prefs

class App : Application() {

    override fun onCreate() {
        super.onCreate()
        instance = this
        Prefs.init(this)
        Library.init(this)
    }

    companion object {
        lateinit var instance: App
            private set
    }
}
