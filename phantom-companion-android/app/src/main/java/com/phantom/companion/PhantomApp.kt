package com.phantom.companion

import android.app.Application
import com.phantom.companion.di.AppContainer

class PhantomApp : Application() {

    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        instance = this
        container = AppContainer(this)
    }

    companion object {
        lateinit var instance: PhantomApp
            private set
    }
}
