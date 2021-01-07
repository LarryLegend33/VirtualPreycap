(ns vpc_stimuli.core
  (:require [quil.core :as q]
            [quil.middleware :as m]
            [langohr.core :as rmq]
            [langohr.channel :as lch]
            [langohr.queue :as lq]
            [langohr.consumers :as lc]
            [langohr.basic :as lb]
            [genartlib.random :as gen]
            [clojure.math.numeric-tower :as math]))

(def ^ {:const true}
  default-exchange-name "")

(def amqp-url (get (System/getenv) "amqp://guest:guest@localhost:5672"))

(defn message-handler
  [ch {:keys [content-type delivery-tag type] :as meta} ^bytes payload]
  (println (format "[consumer] Received a message: %s, delivery tag: %d, content type: %s, type: %s"
                   (String. payload "UTF-8") delivery-tag content-type type)))


(defn setup []
  ; Set frame rate to 30 frames per second.
  (q/frame-rate 30)
  ; Set color mode to HSB (HSV) instead of default RGB.
  (q/color-mode :hsb)
  ; setup function returns initial state. It contains
  ; circle color and position.
  {:color 0
   :angle 0})

(defn update-state [state]
  ; Update sketch state by changing circle color and position.
  {:color (mod (+ (:color state) 0.7) 255)
   :angle (+ (:angle state) 0.1)})

(defn draw-state [state]
  ; Clear the sketch by filling it with light-grey color.
  (q/background 240)
  ; Set circle color.
  (q/fill (:color state) 255 255)
  ; Calculate x and y coordinates of the circle.
  (let [angle (:angle state)
        x (* 150 (q/cos angle))
        y (* 150 (q/sin angle))]
    ; Move origin point to the center of the sketch.
    (q/with-translation [(/ (q/width) 2)
                         (/ (q/height) 2)]
      ; Draw the circle.
      (q/ellipse x y 100 100))))


; (q/defsketch vpc_stimuli
;   :title "You spin my circle right round"
;   :size [500 500]
;   ; setup function called only once, during sketch initialization.
;   :setup setup
;   ; update-state is called on each iteration before draw-state.
;   :update update-state
;   :draw draw-state
;   :features [:keep-on-top]
;   ; This sketch uses functional-mode middleware.
;   ; Check quil wiki for more info about middlewares and particularly
;   ; fun-mode.
;   :middleware [m/fun-mode])

  (defn -main
    [& args]
    (let [conn  (rmq/connect)
          ch    (lch/open conn)
          qname "camera1"]
      (println (format "[main] Connected. Channel id: %d" (.getChannelNumber ch)))
      (lq/declare ch qname {:exclusive false :auto-delete true :arguments {"x-max-length" 19}})
      (lc/subscribe ch qname message-handler {:auto-ack true})
      (Thread/sleep 20000)
      (println "[main] Disconnecting...")
      (rmq/close ch)
      (rmq/close conn)))