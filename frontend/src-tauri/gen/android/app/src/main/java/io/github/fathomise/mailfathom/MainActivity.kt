package io.github.fathomise.mailfathom

import android.content.pm.ActivityInfo
import android.content.pm.PackageManager
import android.hardware.Sensor
import android.hardware.SensorManager
import android.os.Build
import android.os.Bundle
import androidx.activity.enableEdgeToEdge
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat

class MainActivity : TauriActivity() {
  /**
   * Hands the system back gesture to the page rather than to the activity, which is the whole of what this head does
   * about it. Tauri's own activity turns this off, so back finishes the application from wherever a reader happens to
   * be — a conversation, a drawer, an open dialog — and there is nothing the client could answer instead. Turned on,
   * the WebView consumes one session-history entry per press and only finishes the activity once there is none left,
   * which is exactly what a browser does with the same gesture.
   *
   * That is why nothing in `frontend/src/` branches on this head for it: the shell is configured to deliver back the
   * way every other head already delivers it, and `Client.App/src/shellOperations/backNavigation.ts` is the one
   * implementation all three share.
   */
  override val handleBackNavigation: Boolean = true

  override fun onCreate(savedInstanceState: Bundle?) {
    enableEdgeToEdge()
    super.onCreate(savedInstanceState)
    keepAPhoneUpright()
    keepThePageClearOfTheSystemBars()
  }

  /**
   * Holds a phone in portrait, and leaves every other device free to rotate.
   *
   * A phone on its side leaves the page about 390 pixels of height, less with the keyboard open, and the design draws
   * no composition for that. A tablet and a foldable are the opposite case: they are read upright or sideways as they
   * are held, and a foldable is often opened straight into the posture its reader wants.
   *
   * That is why this is decided here rather than in the manifest. Android 16 already ignores an orientation
   * restriction on a display at least 600 dp wide for an application targeting API 36 or later, but a foldable's
   * cover screen is narrower than that and a manifest attribute cannot tell it from a phone. A hinge can: a device
   * that reports one is never locked, in either state, so opening it never finds the inner screen held upright by
   * what the cover screen decided.
   *
   * The hinge is asked of the sensor as well as of the declared feature, because the sensor is what the platform's own
   * fold postures are read from and a device can carry it without declaring the feature — the emulator's foldables do.
   *
   * The width read is the display's rather than the window's, so a tablet that started this activity in a split
   * screen is not mistaken for a phone once it takes the whole display.
   */
  private fun keepAPhoneUpright() {
    val sensors = getSystemService(SensorManager::class.java)
    val isFoldable = packageManager.hasSystemFeature(PackageManager.FEATURE_SENSOR_HINGE_ANGLE) ||
      sensors?.getDefaultSensor(Sensor.TYPE_HINGE_ANGLE) != null
    if (!isFoldable && smallestDisplayWidthInDp() < 600) {
      requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
    }
  }

  private fun smallestDisplayWidthInDp(): Int {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
      return resources.configuration.smallestScreenWidthDp
    }

    val bounds = windowManager.maximumWindowMetrics.bounds
    return (minOf(bounds.width(), bounds.height()) / resources.displayMetrics.density).toInt()
  }

  /**
   * Pads the activity's content by whatever the status bar, the navigation bar, a display cutout and the keyboard
   * occupy, so the page is laid out in the space actually left to it.
   *
   * The client already asks for this the way the web platform offers it — `viewport-fit=cover` on the document and
   * `env(safe-area-inset-*)` behind the `--spacing-safe-*` tokens in `Client.App/src/styles.css`. Android's WebView
   * does not answer: it reports those insets for a display cutout alone and never for the system bars, so on a window
   * that `enableEdgeToEdge` above made full-bleed every one of them reads `0px` while the WebView spans the whole
   * display. What that costs is the top of whichever screen is open — the mail space's selection bar and its toolbar
   * sit under the clock, and the bottom navigation sits under the gesture handle.
   *
   * Answering it here rather than in the page is what keeps the other two heads out of it. Nothing in `frontend/src/`
   * learns that a third head exists, and a desktop or web window keeps reading the same tokens for the case they were
   * written for.
   *
   * The insets are consumed rather than passed on, because this view is the root the page is laid out inside and
   * anything below it would be padding a second time for the same bars.
   *
   * What padding costs is the strip behind each bar, which the window's own background paints rather than the page —
   * so a head whose theme is set against the system's shows the system's colour there. Lifting that means handing the
   * measured values to the page as the `--spacing-safe-*` tokens instead of padding at all, which needs a reference to
   * the WebView and a first delivery that cannot race the load. It is worth doing when the strip is worth it;
   * a nightly head drawing the right screens is worth more.
   */
  private fun keepThePageClearOfTheSystemBars() {
    ViewCompat.setOnApplyWindowInsetsListener(findViewById(android.R.id.content)) { view, insets ->
      val occupied = insets.getInsets(
        WindowInsetsCompat.Type.systemBars() or
          WindowInsetsCompat.Type.displayCutout() or
          WindowInsetsCompat.Type.ime(),
      )
      view.setPadding(occupied.left, occupied.top, occupied.right, occupied.bottom)

      WindowInsetsCompat.CONSUMED
    }
  }
}
